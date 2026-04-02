// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.InteropServices;

namespace Cosmos.Kernel.Core.Memory.GarbageCollector;

// constant defined in:
// https://github.com/dotnet/runtime/blob/ecc5874fbe7c2f2db3cc7e563bc6e81c7a2c17f6/src/coreclr/gc/gcconfig.h#L65

/// <summary>
/// Information methods
/// </summary>
public static unsafe partial class GarbageCollector
{
    /// <summary>
    /// Simple snapshot of GC memory statistics used by runtime memory queries.
    /// </summary>
    public struct SimpleMemoryInfo
    {
        public ulong MemoryLoadBytes;
        public ulong HeapSizeBytes;
        public ulong FragmentedBytes;
        public ulong TotalCommittedBytes;
        public ulong PromotedBytes;
        public ulong PinnedObjectsCount;
        public int CollectionIndex;
        public int CondemnedGeneration;
    }

    // staticly define for now
    static Dictionary<string, object>? variables = null;
    public static IReadOnlyDictionary<string, object> Variables
    {
        get
        {
            if (!s_initialized)
            {
                return null!; // the GC configs variables are available only after GC initialization
            }

            if (variables == null)
            {
                variables = new Dictionary<string, object>()
                {
                    // "Whether we should be using Server GC"
                    ["gcServer"] = false,
                    // Whether we should be using Concurrent GC
                    ["gcConcurrent"] = false,
                    // When set we put the segments that should be deleted on a standby list (instead of
                    // releasing them back to the OS) which will be considered to satisfy new segment requests
                    // (note that the same thing can be specified via API which is the supported way)
                    ["GCRetainVM"] = false,
                    // If set, do not affinitize server GC threads
                    ["GCNoAffinitize"] = false,
                    // Enables CPU groups in the GC
                    ["GCCpuGroup"] = false,
                    // Enables using Large Pages in the GC
                    ["GCLargePages"] = false,
                    // Specifies the size that will make objects go on LOH
                    //["GCLOHThreshold"] = 0,
                    // Specifies the number of server GC heaps
                    //["GCHeapCount"] = 0,
                    // Specifies the max number of server GC heaps to adjust to
                    //["GCMaxHeapCount"] = 0,
                    // Specifies processor mask for Server GC threads
                    //["GCHeapAffinitizeMask"] = 0,
                    // Specifies list of processors for Server GC threads. The format is a comma separated list of
                    // processor numbers or ranges of processor numbers. Format need to be specified in the doc.
                    //["GCHeapAffinitizeRanges"] = "",
                    // The percent for GC to consider as high memory
                    //["GCHighMemPercent"] = 0,
                    // Specifies the largest gen0 allocation budget
                    //["GCGen0MaxBudget"] = 0,
                    // Specifies a hard limit for the GC heap
                    //["GCHeapHardLimit"] = 0,
                    // Specifies the GC heap usage as a percentage of the total memory
                    //["GCHeapHardLimitPercent"] = 0,
                    // Specifies the range for the GC heap
                    //["GCRegionRange"] = 0,
                    // Specifies the size for a basic GC region
                    //["GCRegionSize"] = 0,
                    // UOH allocation during a BGC waits till end of BGC after UOH increases by this percent
                    //["UOHWaitBGCSizeIncPercent"] = -1,
                    // Specifies a hard limit for the GC heap SOH
                    //["GCHeapHardLimitSOH"] = 0,
                    // Specifies a hard limit for the GC heap LOH
                    //["GCHeapHardLimitLOH"] = 0,
                    // Specifies a hard limit for the GC heap POH
                    //["GCHeapHardLimitPOH"] = 0,
                    // Specifies the GC heap SOH usage as a percentage of the total memory
                    //["GCHeapHardLimitSOHPercent"] = 0,
                    // Specifies the GC heap LOH usage as a percentage of the total memory
                    //["GCHeapHardLimitLOHPercent"] = 0,
                    // Specifies the GC heap POH usage as a percentage of the total memory
                    //["GCHeapHardLimitPOHPercent"] = 0,
                    // Specifies how hard GC should try to conserve memory - values 0-9
                    //["GCConserveMemory"] = 0,
                    // Specifies the name of the standalone GC implementation.
                    ["GCName"] = "OrionGC",
                    // Specifies the path of the standalone GC implementation.
                    ["GCPath"] = "",
                    // Enable the GC to dynamically adapt to application sizes.
                    //["GCDynamicAdaptationMode"] = 1,
                    // Specifies the target tcp for DATAS
                    //["GCDTargetTCP"] = 0,
                    // Specifies the percentage of the default growth factor
                    //["GCDGen0GrowthPercent"] = 0,
                    // Specifies the minimum growth factor in permil
                    //["GCDGen0GrowthMinFactor"] = 0,
                    // Specifies the maximum growth factor in permil
                    //["GCDGen0GrowthMaxFactor"] = 0,
                };
            }
            return variables!;
        }
    }

    public static ulong GetHeapSizeBytes()
    {
        if (!s_initialized)
        {
            return 0;
        }

        ulong heapSize = 0;
        for (GCSegment* seg = s_segments; seg != null; seg = seg->Next)
        {
            heapSize += (ulong)(seg->Bump - seg->Start);
        }

        return heapSize;
    }

    public static ulong GetCommittedGcSegmentsBytes()
    {
        if (!s_initialized)
        {
            return 0;
        }

        ulong committedGcSegments = 0;
        for (GCSegment* seg = s_segments; seg != null; seg = seg->Next)
        {
            committedGcSegments += seg->TotalSize;
        }

        return committedGcSegments;
    }

    public static ulong GetFragmentedBytes()
    {
        if (!s_initialized)
        {
            return 0;
        }

        ulong fragmented = 0;
        if (s_freeListsInitialized && s_freeLists != null)
        {
            for (int i = 0; i < NumSizeClasses; i++)
            {
                FreeBlock* cur = s_freeLists[i];
                while (cur != null)
                {
                    fragmented += (uint)cur->Size;
                    cur = cur->Next;
                }
            }
        }

        return fragmented;
    }

    public static ulong GetPinnedObjectsCount()
    {
        if (!s_initialized)
        {
            return 0;
        }

        ulong pinned = 0;
        if (s_handlerStore != null)
        {
            int size = (int)(s_handlerStore->End - s_handlerStore->Bump) / sizeof(GCHandle);
            var handles = new Span<GCHandle>(s_handlerStore->Bump, size);
            foreach (var handle in handles)
            {
                if ((IntPtr)handle.obj == IntPtr.Zero)
                {
                    continue;
                }

                if (handle.type == GCHandleType.Pinned)
                {
                    pinned++;
                }
            }
        }

        return pinned;
    }

    public static ulong GetTotalCommittedBytes()
    {
        if (!s_initialized)
        {
            return 0;
        }

        ulong totalCommitted = GetCommittedGcSegmentsBytes();

        // Pinned segments
        for (GCSegment* seg = s_pinnedSegments; seg != null; seg = seg->Next)
        {
            totalCommitted += seg->TotalSize;
        }

        // Frozen segments (committed size)
        FrozenSegmentInfo* fseg = s_frozenSegments;
        while (fseg != null)
        {
            totalCommitted += (ulong)fseg->CommitSize;
            fseg = fseg->Next;
        }

        // Mark stack pages
        totalCommitted += s_markStackPageCount * PageAllocator.PageSize;

        // Free lists array (one page allocated during init)
        if (s_freeListsInitialized && s_freeLists != null)
        {
            totalCommitted += PageAllocator.PageSize;
        }

        // Handler store segment
        if (s_handlerStore != null)
        {
            totalCommitted += s_handlerStore->TotalSize;
        }

        return totalCommitted;
    }

    public static ulong GetPromotedBytes()
    {
        // Not generational yet
        return 0;
    }

    public static ulong GetMemoryLoadBytes()
    {
        ulong totalPages = PageAllocator.TotalPageCount;
        ulong freePages = PageAllocator.FreePageCount;
        ulong usedPages = totalPages - freePages;
        return usedPages * PageAllocator.PageSize;
    }

    public static int GetCollectionIndex()
    {
        return s_totalCollections;
    }

    public static int GetCondemnedGeneration()
    {
        return 0; // only gen 0 exists currently
    }

    /// <summary>
    /// Determine the generation an object belongs to.
    /// Returns 0 for normal GC-managed allocations, and int.MaxValue for
    /// objects that do not belong to any GC-managed generation (frozen segments,
    /// unmanaged pages, or unknown addresses).
    /// </summary>
    public static int GetObjectGeneration(nint objPtr)
    {
        if (objPtr == 0)
        {
            return int.MaxValue;
        }

        // Frozen segments are not part of GC generations
        if (IsInFrozenSegment(objPtr))
        {
            return int.MaxValue;
        }

        byte* p = (byte*)objPtr;

        // Check regular GC segments (bump-allocated)
        for (GCSegment* seg = s_segments; seg != null; seg = seg->Next)
        {
            if (p >= seg->Start && p < seg->Bump)
            {
                return 0;
            }
        }

        // Check pinned segments
        for (GCSegment* seg = s_pinnedSegments; seg != null; seg = seg->Next)
        {
            if (p >= seg->Start && p < seg->Bump)
            {
                return 0;
            }
        }

        // Check page-based heaps (small/medium/large/SMT)
        byte* ramStart = PageAllocator.RamStart;
        if (p >= ramStart && p < ramStart + PageAllocator.RamSize)
        {
            ulong pageIdx = (ulong)(p - ramStart) / PageAllocator.PageSize;
            if (pageIdx < PageAllocator.TotalPageCount)
            {
                PageType type = PageAllocator.GetPageType(ramStart + pageIdx * PageAllocator.PageSize);
                switch (type)
                {
                    case PageType.HeapSmall:
                    case PageType.HeapMedium:
                    case PageType.HeapLarge:
                    case PageType.SMT:
                        return 0;
                    default:
                        break;
                }
            }
        }

        // Not recognized as managed generation (example: if it's frozen)
        return int.MaxValue;
    }

    public static ulong GetLastGenSizeBefore(int gen)
    {
        if (gen != 0)
        {
            return 0;
        }

        return s_lastGen0SizeBefore;
    }

    public static ulong GetLastGenFragmentationBefore(int gen)
    {
        if (gen != 0)
        {
            return 0;
        }

        return s_lastGen0FragmentationBefore;
    }

    public static ulong GetLastGenSizeAfter(int gen)
    {
        if (gen != 0)
        {
            return 0;
        }

        return s_lastGen0SizeAfter;
    }

    public static ulong GetLastGenFragmentationAfter(int gen)
    {
        if (gen != 0)
        {
            return 0;
        }

        return s_lastGen0FragmentationAfter;
    }

    /// <summary>
    /// Returns the configured per-segment GC size in bytes.
    /// </summary>
    public static ulong GetGCSegmentSizeBytes()
    {
        // s_maxSegmentSize is a uint containing the configured segment size.
        return s_maxSegmentSize;
    }

    /// <summary>
    /// Populate a lightweight memory info snapshot.
    /// Provide a best-effort implementation of RhGetMemoryInfo based on the GC state.
    /// </summary>
    public static SimpleMemoryInfo GetSimpleMemoryInfo()
    {
        SimpleMemoryInfo info = default;
        if (!s_initialized)
        {
            return info;
        }

        info.MemoryLoadBytes = GetMemoryLoadBytes();
        info.HeapSizeBytes = GetHeapSizeBytes();
        info.FragmentedBytes = GetFragmentedBytes();
        info.PinnedObjectsCount = GetPinnedObjectsCount();
        info.TotalCommittedBytes = GetTotalCommittedBytes();
        info.PromotedBytes = GetPromotedBytes();
        info.CollectionIndex = GetCollectionIndex();
        info.CondemnedGeneration = GetCondemnedGeneration();

        return info;
    }

    /// <summary>
    /// Returns percentage of time spent in GC during the last GC interval (0-100).
    /// </summary>
    public static int GetLastGCPercentTimeInGC()
    {
        if (s_lastGCDurationTicks == 0 || s_lastGCIntervalTicks == 0)
        {
            return 0;
        }

        // percent = duration / interval * 100
        long percent = (s_lastGCDurationTicks * 100) / s_lastGCIntervalTicks;
        if (percent > 100)
        {
            percent = 100;
        }
        if (percent < 0)
        {
            percent = 0;
        }
        return (int)percent;
    }

    /// <summary>
    /// Returns the total size in bytes of the specified generation.
    /// This GC is non-generational currently, so generation 0 returns
    /// the current occupied range in regular GC segments.
    /// Other generations return 0.
    /// </summary>
    public static uint GetGenerationSize(int gen)
    {
        if (gen != 0)
        {
            return 0;
        }

        uint used = 0;
        for (GCSegment* seg = s_segments; seg != null; seg = seg->Next)
        {
            used += (uint)(seg->Bump - seg->Start);
        }

        return used;
    }

    /// <summary>
    /// Returns the total size in bytes of the specified generation.
    /// Computes current fragmentation by summing sizes of free blocks in all free lists.
    /// Other generations return 0.
    /// </summary>
    public static ulong GetCurrentFragmentation(int gen)
    {
        if (gen != 0)
        {
            return 0;
        }

        ulong fragmented = 0;
        if (s_freeListsInitialized && s_freeLists != null)
        {
            for (int i = 0; i < NumSizeClasses; i++)
            {
                FreeBlock* cur = s_freeLists[i];
                while (cur != null)
                {
                    fragmented += (uint)cur->Size;
                    cur = cur->Next;
                }
            }
        }

        return fragmented;
    }

    /// <summary>
    /// Gets cumulative GC statistics.
    /// </summary>
    /// <param name="totalCollections">Total number of collections performed.</param>
    /// <param name="totalObjectsFreed">Total number of objects freed across all collections.</param>
    public static void GetStats(out int totalCollections, out int totalObjectsFreed)
    {
        totalCollections = s_totalCollections;
        totalObjectsFreed = s_totalObjectsFreed;
    }

}
