using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.Core.Memory.GarbageCollector;
using KernelHeap = Cosmos.Kernel.Core.Memory.Heap.Heap;

namespace Cosmos.Kernel.System.Diagnostics;

/// <summary>
/// Read-only view of the kernel's physical-memory and garbage-collector
/// statistics, plus a forced collection. All reads are plain field reads
/// with no allocation, so they are safe to poll from a monitor loop at
/// display refresh rate. Managed-heap figures beyond these counters come
/// from the BCL (<see cref="global::System.GC.GetGCMemoryInfo()"/>).
/// </summary>
public static class MemoryInfo
{
    /// <summary>
    /// Size of a physical page in bytes.
    /// </summary>
    public static ulong PageSizeBytes => PageAllocator.PageSize;

    /// <summary>
    /// Total number of physical pages managed by the page allocator.
    /// </summary>
    public static ulong TotalPages => PageAllocator.TotalPageCount;

    /// <summary>
    /// Number of physical pages currently free.
    /// </summary>
    public static ulong FreePages => PageAllocator.FreePageCount;

    /// <summary>
    /// Total usable RAM in bytes, as reported by the bootloader memory map.
    /// </summary>
    public static ulong RamSizeBytes => PageAllocator.RamSize;

    /// <summary>
    /// Percentage of time spent in the garbage collector during the most
    /// recent collection window, from 0 to 100.
    /// </summary>
    public static int GcTimePercent => GarbageCollector.GetLastGCPercentTimeInGC();

    /// <summary>
    /// Returns the kernel garbage collector's lifetime counters.
    /// </summary>
    /// <param name="totalCollections">Number of collections since boot.</param>
    /// <param name="totalObjectsFreed">Number of objects freed since boot.</param>
    public static void GetGcStats(out int totalCollections, out int totalObjectsFreed) =>
        GarbageCollector.GetStats(out totalCollections, out totalObjectsFreed);

    /// <summary>
    /// Forces an immediate garbage collection and returns the number of
    /// objects freed. Unlike <see cref="global::System.GC.Collect()"/>,
    /// the count of freed objects is reported back to the caller.
    /// </summary>
    /// <returns>Number of objects freed by the collection.</returns>
    public static int Collect() => KernelHeap.Collect();
}
