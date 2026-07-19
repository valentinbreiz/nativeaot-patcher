// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.CompilerServices;
using Cosmos.Kernel.Boot.Limine;
using Cosmos.Kernel.Core.ARM64.Bridge;
using Cosmos.Kernel.Core.IO;

namespace Cosmos.Kernel.Core.ARM64.Cpu;

/// <summary>
/// Adds Device MMIO mappings into Limine's existing TTBR1 page tables.
/// Limine's HHDM only covers RAM; device MMIO regions (GIC, timers, etc.)
/// are NOT mapped. This class walks the TTBR1 page tables and inserts
/// 2MiB block descriptors with Device-nGnRnE/nGnRE attributes so the
/// HHDM virtual address (phys + HHDM_OFFSET) can be dereferenced for MMIO.
/// Native imports live in Cosmos.Kernel.Core.ARM64/Bridge/Import/DeviceMapperNative.cs.
/// </summary>
public static unsafe class DeviceMapper
{
    // Page table descriptor bits
    private const ulong DESC_VALID = 1UL << 0;
    private const ulong DESC_TABLE = 1UL << 1;  // 1 = table, 0 = block
    private const ulong DESC_AF = 1UL << 10;
    private const ulong DESC_PXN = 1UL << 53;
    private const ulong DESC_UXN = 1UL << 54;
    private const ulong ADDR_MASK = 0x0000FFFFFFFFF000UL;
    private const ulong BLOCK_2MB_ADDR_MASK = 0x0000FFFFFFE00000UL;
    private const ulong BLOCK_1GB_ADDR_MASK = 0x0000FFFFC0000000UL;

    private static bool _spareL2Used;

    // ── Public API ──────────────────────────────────────────────────

    /// <summary>
    /// Ensures a physical MMIO address is mapped as Device memory in the
    /// TTBR1 page tables so it can be accessed at (phys + HHDM_OFFSET).
    /// Safe to call multiple times; no-ops if the mapping already exists.
    /// </summary>
    public static void EnsureMapped(ulong physBase)
    {
        if (Limine.HHDM.Response == null)
        {
            return;
        }

        ulong hhdm = Limine.HHDM.Response->Offset;
        MapPage(physBase, hhdm);
    }

    // ── Core mapping logic ──────────────────────────────────────────

    private static void MapPage(ulong physBase, ulong hhdmOffset)
    {
        // 2MiB-align
        ulong aligned = physBase & BLOCK_2MB_ADDR_MASK;
        ulong virtAddr = aligned + hhdmOffset;

        Serial.Write("[DeviceMapper] Mapping phys 0x");
        Serial.WriteHex(aligned);
        Serial.Write(" → virt 0x");
        Serial.WriteHex(virtAddr);
        Serial.Write("\n");

        // ── Find Device memory MAIR index ────────────────────────
        ulong mair = DeviceMapperNative.ReadMair();
        int deviceIdx = FindDeviceMairIndex(mair);
        if (deviceIdx < 0)
        {
            Serial.Write("[DeviceMapper] ERROR: No Device MAIR index found!\n");
            return;
        }
        Serial.Write("[DeviceMapper] Device MAIR index = ");
        Serial.WriteNumber((uint)deviceIdx);
        Serial.Write("\n");

        // ── Read TTBR1 and walk page tables ──────────────────────
        ulong ttbr1Phys = DeviceMapperNative.ReadTtbr1() & ADDR_MASK;
        ulong* l0 = (ulong*)(ttbr1Phys + hhdmOffset);

        // L0 index (bits [47:39] of the VA offset within TTBR1 space)
        // For TTBR1 with T1SZ=16: VA bits [47:0] are used.
        // The HHDM offset is typically 0xFFFF000000000000, so
        // virtAddr - hhdmOffset = physBase. We index using physBase bits.
        int l0idx = (int)((aligned >> 39) & 0x1FF);
        ulong l0entry = l0[l0idx];

        Serial.Write("[DeviceMapper] L0[");
        Serial.WriteNumber((uint)l0idx);
        Serial.Write("] = 0x");
        Serial.WriteHex(l0entry);
        Serial.Write("\n");

        if ((l0entry & DESC_VALID) == 0)
        {
            Serial.Write("[DeviceMapper] ERROR: L0 entry invalid\n");
            return;
        }
        if ((l0entry & DESC_TABLE) == 0)
        {
            Serial.Write("[DeviceMapper] ERROR: L0 is block (unexpected)\n");
            return;
        }

        // Follow L0 table → L1
        ulong* l1 = (ulong*)((l0entry & ADDR_MASK) + hhdmOffset);
        int l1idx = (int)((aligned >> 30) & 0x1FF);
        ulong l1entry = l1[l1idx];

        Serial.Write("[DeviceMapper] L1[");
        Serial.WriteNumber((uint)l1idx);
        Serial.Write("] = 0x");
        Serial.WriteHex(l1entry);
        Serial.Write("\n");

        ulong* l2;

        if ((l1entry & DESC_VALID) == 0)
        {
            Serial.Write("[DeviceMapper] ERROR: L1 entry invalid\n");
            return;
        }
        else if ((l1entry & DESC_TABLE) != 0)
        {
            // Table descriptor → follow to L2
            l2 = (ulong*)((l1entry & ADDR_MASK) + hhdmOffset);
            Serial.Write("[DeviceMapper] L1 is table → L2 at 0x");
            Serial.WriteHex((ulong)l2);
            Serial.Write("\n");
        }
        else
        {
            // Block descriptor (1GiB) → need to split into L2 table
            Serial.Write("[DeviceMapper] L1 is 1GiB block, splitting...\n");
            l2 = SplitL1Block(l1, l1idx, l1entry, hhdmOffset);
            if (l2 == null)
            {
                Serial.Write("[DeviceMapper] ERROR: Failed to split L1 block\n");
                return;
            }
        }

        // ── Write L2 entry ───────────────────────────────────────
        int l2idx = (int)((aligned >> 21) & 0x1FF);
        ulong l2entry = l2[l2idx];

        Serial.Write("[DeviceMapper] L2[");
        Serial.WriteNumber((uint)l2idx);
        Serial.Write("] = 0x");
        Serial.WriteHex(l2entry);
        Serial.Write("\n");

        // Check if existing mapping already has Device attributes
        if ((l2entry & DESC_VALID) != 0)
        {
            int existingIdx = (int)((l2entry >> 2) & 0x7);
            byte existingAttr = (byte)((mair >> (existingIdx * 8)) & 0xFF);
            if (existingAttr == 0x00 || existingAttr == 0x04)
            {
                Serial.Write("[DeviceMapper] L2 already Device-mapped, skipping\n");
                return;
            }
            Serial.Write("[DeviceMapper] L2 valid but Normal memory (MAIR attr=0x");
            Serial.WriteHex(existingAttr);
            Serial.Write("), BBM replacing with Device\n");

            // ARM Break-Before-Make: must invalidate first, flush TLB,
            // then write the new descriptor. Cannot change attributes in-place.
            l2[l2idx] = 0;  // Step 1: invalidate
            DeviceMapperNative.DsbIsb();
            DeviceMapperNative.FlushTlb(virtAddr >> 12);  // Step 2: flush stale TLB
            DeviceMapperNative.DsbIsb();
        }

        // Build 2MiB block descriptor with Device attributes
        // Bits: Valid=1, Block(bit1=0), AttrIndx[4:2], AF[10], PXN[53], UXN[54]
        ulong desc = (aligned & BLOCK_2MB_ADDR_MASK)
                   | ((ulong)deviceIdx << 2)
                   | DESC_AF
                   | DESC_PXN
                   | DESC_UXN
                   | DESC_VALID;

        Serial.Write("[DeviceMapper] Writing L2 descriptor: 0x");
        Serial.WriteHex(desc);
        Serial.Write("\n");

        // Step 3: write new descriptor with Device attributes
        l2[l2idx] = desc;

        // Ensure descriptor is visible before use
        DeviceMapperNative.DsbIsb();

        // Final TLB flush for the new mapping
        DeviceMapperNative.FlushTlb(virtAddr >> 12);

        Serial.Write("[DeviceMapper] Mapping complete\n");
    }

    /// <summary>
    /// Splits a 1GiB L1 block descriptor into 512 × 2MiB L2 block descriptors,
    /// preserving the original attributes for all entries.
    /// </summary>
    private static ulong* SplitL1Block(ulong* l1, int l1idx, ulong l1entry, ulong hhdmOffset)
    {
        if (_spareL2Used)
        {
            Serial.Write("[DeviceMapper] ERROR: Spare L2 table already used\n");
            return null;
        }

        // Get pre-allocated L2 table virtual address
        ulong l2va = DeviceMapperNative.GetSpareL2TableAddr();
        if (l2va == 0)
        {
            return null;
        }

        // Get its physical address (for the L1 table descriptor)
        ulong l2pa = DeviceMapperNative.VirtToPhys(l2va);
        if (l2pa == 0)
        {
            Serial.Write("[DeviceMapper] ERROR: Cannot translate spare L2 table VA\n");
            return null;
        }

        ulong* l2 = (ulong*)l2va;

        // Extract the 1GiB block's physical base and attributes
        ulong blockPhysBase = l1entry & BLOCK_1GB_ADDR_MASK;
        // Lower attributes [11:2] (AttrIndx, NS, AP, SH, AF, nG)
        ulong lowerAttrs = l1entry & 0xFFC;
        // Upper attributes [54:52] (PXN, UXN, Contiguous)
        ulong upperAttrs = l1entry & 0x0070000000000000UL;

        // Fill 512 entries as 2MiB blocks with the same attributes
        for (int i = 0; i < 512; i++)
        {
            ulong entryPhys = blockPhysBase + ((ulong)i << 21);
            l2[i] = entryPhys | lowerAttrs | upperAttrs | DESC_VALID; // bit1=0 → block
        }

        // Ensure all L2 entries are written before updating L1
        DeviceMapperNative.DsbIsb();

        // Replace L1 block with table descriptor pointing to L2
        // Table descriptor: PA | 0x3 (valid + table)
        l1[l1idx] = l2pa | DESC_VALID | DESC_TABLE;

        DeviceMapperNative.DsbIsb();

        // Flush entire TLB since we changed a 1GiB mapping
        // (vale1 only flushes one page; we need broader flush)
        DeviceMapperNative.FlushTlb(0); // will be followed by individual flushes if needed

        _spareL2Used = true;

        Serial.Write("[DeviceMapper] Split L1 block into L2 table at PA 0x");
        Serial.WriteHex(l2pa);
        Serial.Write("\n");

        return l2;
    }

    /// <summary>
    /// Scans MAIR_EL1 for a Device memory attribute index.
    /// Looks for 0x00 (Device-nGnRnE) or 0x04 (Device-nGnRE).
    /// </summary>
    private static int FindDeviceMairIndex(ulong mair)
    {
        for (int i = 0; i < 8; i++)
        {
            byte attr = (byte)((mair >> (i * 8)) & 0xFF);
            if (attr == 0x00 || attr == 0x04)
            {
                return i;
            }
        }
        return -1;
    }
}
