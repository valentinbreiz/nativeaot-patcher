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
        public ulong HeapSizeBytes;
        public ulong FragmentedBytes;
        public ulong TotalCommittedBytes;
        public ulong PromotedBytes;
        public ulong PinnedObjectsCount;
        public int CollectionIndex;
        public int CondemnedGeneration;
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
    /// Populate a lightweight memory info snapshot.
    /// Provide a best-effort implementation of RhGetMemoryInfo based on the GC state.
    /// </summary>
    public static SimpleMemoryInfo GetSimpleMemoryInfo()
    { 
        SimpleMemoryInfo info = default;

        // Compute heap size from current occupied range in GC segments
        // (from segment start up to bump pointer).
        ulong heapSize = 0;
        ulong committedGcSegments = 0;
        for (GCSegment* seg = s_segments; seg != null; seg = seg->Next)
        {
            heapSize += (ulong)(seg->Bump - seg->Start);
            committedGcSegments += seg->TotalSize;
        }
        info.HeapSizeBytes = heapSize;

        // Compute fragmentation by summing free block sizes from free lists
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
        info.FragmentedBytes = fragmented;

        // Count pinned handles
        ulong pinned = 0;
        if (s_handlerStore != null)
        {
            // Walk the handler store using the actual GCHandle size
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
        info.PinnedObjectsCount = pinned;

        // Compute total committed bytes: include committed GC segments, pinned segments, frozen committed pages,
        // mark stack pages, free-lists page, and handler store.
        ulong totalCommitted = committedGcSegments;

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

        info.TotalCommittedBytes = totalCommitted;
        // no other genration, so the bytes keep in the same genration
        info.PromotedBytes = 0; 
        info.CollectionIndex = s_totalCollections;
        // only 0 generation exist
        info.CondemnedGeneration = 0;

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
