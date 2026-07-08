// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Boot.Limine;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.Core.X64.Bridge;

namespace Cosmos.Kernel.Core.X64.Cpu;

/// <summary>
/// Adds MMIO mappings into Limine's existing page tables on demand, the x64
/// counterpart of the ARM64 <c>DeviceMapper</c>. Limine's blanket map (base
/// revision 0) covers the low 4 GiB plus memory-map regions, so MMIO below
/// 4 GiB is already reachable through the HHDM — but 64-bit BARs relocated
/// above 4 GiB are in no memory-map region and their HHDM alias page-faults.
/// This class walks CR3's 4-level tables and inserts 2 MiB uncacheable
/// (PCD|PWT) mappings so <c>phys + HHDM offset</c> dereferences work for
/// any BAR placement. Already-present mappings are left untouched: the low
/// 4 GiB keeps Limine's attributes (MTRRs make QEMU/PC MMIO correct there
/// today, and rewriting live Limine entries is not worth the risk).
/// </summary>
public static unsafe class DeviceMapper
{
    private const ulong FlagPresent = 1UL << 0;
    private const ulong FlagWritable = 1UL << 1;
    private const ulong FlagWriteThrough = 1UL << 3;
    private const ulong FlagCacheDisable = 1UL << 4;
    private const ulong FlagPageSize = 1UL << 7;
    private const ulong FlagNoExecute = 1UL << 63;

    // Physical-address field of a table entry (bits 51:12).
    private const ulong AddrMask = 0x000F_FFFF_FFFF_F000;
    // 2 MiB alignment of a physical address (low 21 bits cleared).
    private const ulong Align2MiB = 0xFFFF_FFFF_FFE0_0000;

    /// <summary>
    /// Ensures the 2 MiB block containing <paramref name="physBase"/> is
    /// mapped at (phys + HHDM offset). No-op when the block is already
    /// mapped (in particular the whole Limine-covered low 4 GiB). Safe to
    /// call multiple times.
    /// </summary>
    public static void EnsureMapped(ulong physBase)
    {
        if (Limine.HHDM.Response == null)
        {
            return;
        }

        ulong hhdm = Limine.HHDM.Response->Offset;
        ulong alignedPhys = physBase & Align2MiB;
        ulong virt = alignedPhys + hhdm;

        // The tables themselves live in low RAM, which the HHDM covers.
        ulong* pml4 = (ulong*)((X64CpuNative.ReadCr3() & AddrMask) + hhdm);

        ulong* pdpt = GetOrCreateTable(pml4, (int)((virt >> 39) & 0x1FF), hhdm);
        if (pdpt == null)
        {
            return;
        }

        int pdptIndex = (int)((virt >> 30) & 0x1FF);
        ulong pdptEntry = pdpt[pdptIndex];
        if ((pdptEntry & FlagPresent) != 0 && (pdptEntry & FlagPageSize) != 0)
        {
            // 1 GiB page already covers this block (Limine's low-4-GiB map).
            return;
        }

        ulong* pd = GetOrCreateTable(pdpt, pdptIndex, hhdm);
        if (pd == null)
        {
            return;
        }

        int pdIndex = (int)((virt >> 21) & 0x1FF);
        if ((pd[pdIndex] & FlagPresent) != 0)
        {
            // A 2 MiB page or a 4 KiB table already maps this block.
            return;
        }

        Serial.WriteString("[DeviceMapper] Mapping MMIO phys 0x");
        Serial.WriteHex(alignedPhys);
        Serial.WriteString(" -> virt 0x");
        Serial.WriteHex(virt);
        Serial.WriteString(" (2MiB, UC)\n");

        // Uncacheable (PCD|PWT -> PAT UC) and non-executable: device
        // registers must not be prefetched, combined, or fetched as code.
        pd[pdIndex] = alignedPhys | FlagPresent | FlagWritable
                    | FlagCacheDisable | FlagWriteThrough
                    | FlagPageSize | FlagNoExecute;
        X64CpuNative.InvalidatePage(virt);
    }

    /// <summary>
    /// Follows <paramref name="parent"/>[<paramref name="index"/>] to its
    /// child table, allocating and linking a zeroed one when the entry is
    /// not present. Returns null when the entry is a huge page (caller
    /// handles that as already-mapped) or the allocation fails.
    /// </summary>
    private static ulong* GetOrCreateTable(ulong* parent, int index, ulong hhdm)
    {
        ulong entry = parent[index];
        if ((entry & FlagPresent) != 0)
        {
            if ((entry & FlagPageSize) != 0)
            {
                return null;
            }

            return (ulong*)((entry & AddrMask) + hhdm);
        }

        void* page = PageAllocator.AllocPages(PageType.PageDirectory, 1, zero: true);
        if (page == null)
        {
            Serial.WriteString("[DeviceMapper] ERROR: page-table allocation failed\n");
            return null;
        }

        ulong tablePhys = PageAllocator.VirtualToPhysical((ulong)page);
        parent[index] = tablePhys | FlagPresent | FlagWritable;
        return (ulong*)page;
    }
}
