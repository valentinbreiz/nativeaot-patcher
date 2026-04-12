// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Boot.Limine;

namespace Cosmos.Kernel.Core.Memory;

/// <summary>
/// Inserts Device-memory mappings into the bootloader-provided kernel pagemap.
///
/// Limine 11 under protocol base revision 6 does not identity-map the lower
/// half and explicitly excludes RESERVED memmap entries — which is where all
/// MMIO lives (PL011, GIC, LAPIC, IOAPIC, HPET, etc.). Accessing those physical
/// addresses without first installing a mapping is an immediate translation
/// fault on arm64 and a #PF on x86_64.
///
/// <para>This class walks the higher-half pagemap Limine hands off (TTBR1 on
/// arm64, CR3 on x86_64), splits a 1 GiB block into a 2 MiB table when the
/// target PDPT/PD entry is a huge block (reusing a single pre-allocated
/// spare page in <c>.bss</c>), and writes an uncached block descriptor so
/// that <c>phys + Limine.HHDM.Response->Offset</c> becomes a valid virtual
/// address for MMIO.</para>
///
/// <para>This class must stay silent: it is called from
/// <see cref="IO.Serial.ComInit"/> before the UART is usable, so any
/// Serial.Write here would recurse into uninitialised MMIO.</para>
/// </summary>
public static unsafe partial class DeviceMapper
{
    /// <summary>
    /// Ensures a physical MMIO address is mapped as uncached Device memory
    /// in the kernel's higher-half pagemap so it can be dereferenced at
    /// <c>phys + Limine.HHDM.Response-&gt;Offset</c>. Safe to call multiple
    /// times; a no-op if the mapping already exists with the correct
    /// attributes.
    /// </summary>
    /// <returns>
    /// <c>true</c> if the mapping is now in place (or was already),
    /// <c>false</c> if Limine HHDM isn't populated yet or the pagemap walk
    /// couldn't reach the target (e.g. no parent entry, no spare table left
    /// for a split).
    /// </returns>
    public static bool EnsureMapped(ulong physBase)
    {
        if (Limine.HHDM.Response == null)
        {
            return false;
        }

        ulong hhdm = Limine.HHDM.Response->Offset;
        return MapPage(physBase, hhdm);
    }
}

#if ARCH_ARM64

public static unsafe partial class DeviceMapper
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

    // ── Native helpers ──────────────────────────────────────────────

    [LibraryImport("*", EntryPoint = "_native_arm64_read_ttbr1_el1")]
    [SuppressGCTransition]
    private static partial ulong ReadTTBR1();

    [LibraryImport("*", EntryPoint = "_native_arm64_read_mair_el1")]
    [SuppressGCTransition]
    private static partial ulong ReadMAIR();

    [LibraryImport("*", EntryPoint = "_native_arm64_tlbi_vale1")]
    [SuppressGCTransition]
    private static partial void FlushTLB(ulong vaShifted);

    [LibraryImport("*", EntryPoint = "_native_arm64_va_to_pa")]
    [SuppressGCTransition]
    private static partial ulong VirtToPhys(ulong va);

    [LibraryImport("*", EntryPoint = "_native_arm64_spare_l2_table_addr")]
    [SuppressGCTransition]
    private static partial ulong GetSpareL2TableAddr();

    [LibraryImport("*", EntryPoint = "_native_arm64_dsb_isb")]
    [SuppressGCTransition]
    private static partial void DsbIsb();

    // ── Core mapping logic ──────────────────────────────────────────

    private static bool MapPage(ulong physBase, ulong hhdmOffset)
    {
        // 2 MiB-align the physical address
        ulong aligned = physBase & BLOCK_2MB_ADDR_MASK;
        ulong virtAddr = aligned + hhdmOffset;

        // Find a Device-memory MAIR attribute index (0x00 or 0x04)
        ulong mair = ReadMAIR();
        int deviceIdx = FindDeviceMairIndex(mair);
        if (deviceIdx < 0)
        {
            return false;
        }

        // Walk TTBR1 via HHDM
        ulong ttbr1Phys = ReadTTBR1() & ADDR_MASK;
        ulong* l0 = (ulong*)(ttbr1Phys + hhdmOffset);

        int l0idx = (int)((aligned >> 39) & 0x1FF);
        ulong l0entry = l0[l0idx];
        if ((l0entry & DESC_VALID) == 0 || (l0entry & DESC_TABLE) == 0)
        {
            return false;
        }

        ulong* l1 = (ulong*)((l0entry & ADDR_MASK) + hhdmOffset);
        int l1idx = (int)((aligned >> 30) & 0x1FF);
        ulong l1entry = l1[l1idx];

        ulong* l2;
        if ((l1entry & DESC_VALID) == 0)
        {
            return false;
        }
        else if ((l1entry & DESC_TABLE) != 0)
        {
            l2 = (ulong*)((l1entry & ADDR_MASK) + hhdmOffset);
        }
        else
        {
            // 1 GiB block — split into a 2 MiB L2 table
            l2 = SplitL1Block(l1, l1idx, l1entry, hhdmOffset);
            if (l2 == null)
            {
                return false;
            }
        }

        int l2idx = (int)((aligned >> 21) & 0x1FF);
        ulong l2entry = l2[l2idx];

        // If already Device-mapped, we're done
        if ((l2entry & DESC_VALID) != 0)
        {
            int existingIdx = (int)((l2entry >> 2) & 0x7);
            byte existingAttr = (byte)((mair >> (existingIdx * 8)) & 0xFF);
            if (existingAttr == 0x00 || existingAttr == 0x04)
            {
                return true;
            }

            // ARM Break-Before-Make: clear the entry, flush TLB, then install new
            l2[l2idx] = 0;
            DsbIsb();
            FlushTLB(virtAddr >> 12);
            DsbIsb();
        }

        // Build 2 MiB Device block descriptor
        ulong desc = (aligned & BLOCK_2MB_ADDR_MASK)
                   | ((ulong)deviceIdx << 2)
                   | DESC_AF
                   | DESC_PXN
                   | DESC_UXN
                   | DESC_VALID;

        l2[l2idx] = desc;
        DsbIsb();
        FlushTLB(virtAddr >> 12);
        return true;
    }

    /// <summary>
    /// Splits a 1 GiB L1 block descriptor into 512 × 2 MiB L2 block descriptors,
    /// preserving the original attributes for every entry. Uses the single
    /// pre-allocated spare L2 page in <c>.bss</c>, so at most one split is
    /// supported in a kernel's lifetime — sufficient for Cosmos's MMIO set.
    /// </summary>
    private static ulong* SplitL1Block(ulong* l1, int l1idx, ulong l1entry, ulong hhdmOffset)
    {
        if (_spareL2Used)
        {
            return null;
        }

        ulong l2va = GetSpareL2TableAddr();
        if (l2va == 0)
        {
            return null;
        }

        ulong l2pa = VirtToPhys(l2va);
        if (l2pa == 0)
        {
            return null;
        }

        ulong* l2 = (ulong*)l2va;
        ulong blockPhysBase = l1entry & BLOCK_1GB_ADDR_MASK;
        ulong lowerAttrs = l1entry & 0xFFC;
        ulong upperAttrs = l1entry & 0x0070000000000000UL;

        for (int i = 0; i < 512; i++)
        {
            ulong entryPhys = blockPhysBase + ((ulong)i << 21);
            l2[i] = entryPhys | lowerAttrs | upperAttrs | DESC_VALID;
        }
        DsbIsb();

        l1[l1idx] = l2pa | DESC_VALID | DESC_TABLE;
        DsbIsb();
        FlushTLB(0);

        _spareL2Used = true;
        return l2;
    }

    /// <summary>
    /// Scans MAIR_EL1 for a Device memory attribute index.
    /// Looks for <c>0x00</c> (Device-nGnRnE) or <c>0x04</c> (Device-nGnRE).
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

#endif // ARCH_ARM64

#if ARCH_X64

public static unsafe partial class DeviceMapper
{
    // x86_64 page table entry bits (4-level paging)
    private const ulong PTE_PRESENT = 1UL << 0;
    private const ulong PTE_WRITE = 1UL << 1;
    private const ulong PTE_PWT = 1UL << 3;   // Page Write-Through
    private const ulong PTE_PCD = 1UL << 4;   // Page Cache Disable
    private const ulong PTE_PS = 1UL << 7;    // Page Size (1 = 2 MiB at PD, 1 GiB at PDPT)
    private const ulong PTE_NX = 1UL << 63;   // No-Execute (requires EFER.NXE)
    private const ulong PTE_ADDR_MASK = 0x000FFFFFFFFFF000UL;
    private const ulong PTE_2MB_ADDR_MASK = 0x000FFFFFFFE00000UL;
    private const ulong PTE_1GB_ADDR_MASK = 0x000FFFFFC0000000UL;

    // Mirror of arm64's _spareL2Used.
    private static bool _sparePdUsed;

    // ── Native helpers ──────────────────────────────────────────────

    [LibraryImport("*", EntryPoint = "_native_x64_read_cr3")]
    [SuppressGCTransition]
    private static partial ulong ReadCR3();

    [LibraryImport("*", EntryPoint = "_native_x64_invlpg")]
    [SuppressGCTransition]
    private static partial void InvalidatePage(ulong va);

    [LibraryImport("*", EntryPoint = "_native_x64_spare_pd_table_addr")]
    [SuppressGCTransition]
    private static partial ulong GetSparePdTableAddr();

    // ── Core mapping logic ──────────────────────────────────────────

    private static bool MapPage(ulong physBase, ulong hhdmOffset)
    {
        // 2 MiB-align the physical address
        ulong aligned = physBase & PTE_2MB_ADDR_MASK;
        ulong virtAddr = aligned + hhdmOffset;

        // Walk CR3 via HHDM: PML4 → PDPT → PD
        ulong cr3 = ReadCR3() & PTE_ADDR_MASK;
        ulong* pml4 = (ulong*)(cr3 + hhdmOffset);

        int pml4idx = (int)((virtAddr >> 39) & 0x1FF);
        ulong pml4e = pml4[pml4idx];
        if ((pml4e & PTE_PRESENT) == 0)
        {
            return false;
        }

        ulong* pdpt = (ulong*)((pml4e & PTE_ADDR_MASK) + hhdmOffset);
        int pdptidx = (int)((virtAddr >> 30) & 0x1FF);
        ulong pdpte = pdpt[pdptidx];

        ulong* pd;
        if ((pdpte & PTE_PRESENT) == 0)
        {
            return false;
        }
        else if ((pdpte & PTE_PS) == 0)
        {
            // Table descriptor → follow to PD
            pd = (ulong*)((pdpte & PTE_ADDR_MASK) + hhdmOffset);
        }
        else
        {
            // 1 GiB block → need to split into a PD
            pd = SplitPdptBlock(pdpt, pdptidx, pdpte, hhdmOffset);
            if (pd == null)
            {
                return false;
            }
        }

        int pdidx = (int)((virtAddr >> 21) & 0x1FF);
        ulong pde = pd[pdidx];

        // If already present as a 2 MiB uncached block, we're done
        if ((pde & PTE_PRESENT) != 0 && (pde & PTE_PS) != 0 &&
            (pde & (PTE_PCD | PTE_PWT)) == (PTE_PCD | PTE_PWT))
        {
            return true;
        }

        // Build the new 2 MiB uncached block descriptor
        ulong desc = (aligned & PTE_2MB_ADDR_MASK)
                   | PTE_PRESENT
                   | PTE_WRITE
                   | PTE_PS
                   | PTE_PCD
                   | PTE_PWT
                   | PTE_NX;

        pd[pdidx] = desc;
        InvalidatePage(virtAddr);
        return true;
    }

    /// <summary>
    /// Splits a 1 GiB PDPT block descriptor into 512 × 2 MiB PD block
    /// descriptors, preserving the original attributes for every entry.
    /// Uses the single pre-allocated spare PD page in <c>.bss</c>, so at
    /// most one split is supported in a kernel's lifetime — sufficient
    /// for Cosmos's early MMIO set (LAPIC and IOAPIC sit in the same
    /// 1 GiB range on typical QEMU x86_64 layouts).
    /// </summary>
    private static ulong* SplitPdptBlock(ulong* pdpt, int pdptidx, ulong pdpte, ulong hhdmOffset)
    {
        if (_sparePdUsed)
        {
            return null;
        }

        ulong pdVa = GetSparePdTableAddr();
        if (pdVa == 0)
        {
            return null;
        }

        // The spare page lives at a kernel-linked higher-half virtual address.
        // Translate to a physical address via the executable address request
        // so we can write it into the PDPT as a table descriptor.
        if (Limine.ExecutableAddress.Response == null)
        {
            return null;
        }

        ulong virtBase = Limine.ExecutableAddress.Response->VirtualBase;
        ulong physBase = Limine.ExecutableAddress.Response->PhysicalBase;
        if (pdVa < virtBase)
        {
            return null;
        }
        ulong pdPa = (pdVa - virtBase) + physBase;

        ulong* pd = (ulong*)pdVa;

        // Extract the 1 GiB block's physical base and attribute bits
        ulong blockPhysBase = pdpte & PTE_1GB_ADDR_MASK;
        ulong attrs = pdpte & ~(PTE_1GB_ADDR_MASK | PTE_PS);

        for (int i = 0; i < 512; i++)
        {
            ulong entryPhys = blockPhysBase + ((ulong)i << 21);
            pd[i] = entryPhys | attrs | PTE_PS;
        }

        // Replace the PDPT block with a table descriptor pointing at the PD.
        pdpt[pdptidx] = pdPa | PTE_PRESENT | PTE_WRITE;

        // The 1 GiB block mapping is no longer valid — individual 2 MiB
        // entries will be invalidated by InvalidatePage in MapPage.
        _sparePdUsed = true;
        return pd;
    }
}

#endif // ARCH_X64
