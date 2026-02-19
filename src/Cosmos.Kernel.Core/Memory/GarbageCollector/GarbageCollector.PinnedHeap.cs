// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.CompilerServices;
using Cosmos.Kernel.Core.IO;
using Internal.Runtime;

namespace Cosmos.Kernel.Core.Memory.GarbageCollector;

public static unsafe partial class GarbageCollector
{
    // --- Constants ---

    private const uint PinnedHeapMinSize = (uint)PageAllocator.PageSize; // Minimum 1 page

    // --- Static fields ---

    // Pinned heap segments - separate from regular GC heap
    private static GCSegment* s_pinnedSegments;
    private static GCSegment* s_currentPinnedSegment;

    // --- Methods ---

    /// <summary>
    /// Allocates a pinned object on the pinned heap.
    /// Pinned objects are not subject to compaction but can still be collected.
    /// </summary>
    private static GCObject* AllocPinnedObject(nint size, GC_ALLOC_FLAGS flags)
    {
        uint allocSize = Align((uint)size);
        if (allocSize < MinBlockSize)
        {
            allocSize = MinBlockSize;
        }

        // Try bump allocation in current pinned segment
        void* result = BumpAllocInPinnedSegment(s_currentPinnedSegment, allocSize);
        if (result != null)
        {
            return (GCObject*)result;
        }

        // Need new segment
        GCSegment* newSegment = AllocatePinnedSegment(Math.Max(PinnedHeapMinSize, allocSize + (uint)sizeof(GCSegment)));
        if (newSegment == null)
        {
            return null;
        }

        // Link the segment
        AppendPinnedSegment(newSegment);
        s_currentPinnedSegment = newSegment;

        // Try allocation again
        result = BumpAllocInPinnedSegment(s_currentPinnedSegment, allocSize);
        return (GCObject*)result;
    }

    /// <summary>
    /// Allocates a new pinned heap segment.
    /// </summary>
    private static GCSegment* AllocatePinnedSegment(uint requestedSize)
    {
        uint size = requestedSize < PinnedHeapMinSize ? PinnedHeapMinSize : requestedSize;
        uint totalSize = size + (uint)sizeof(GCSegment);
        ulong pageCount = (totalSize + PageAllocator.PageSize - 1) / PageAllocator.PageSize;

        var memory = (byte*)PageAllocator.AllocPages(PageType.Unmanaged, pageCount, true);
        if (memory == null)
        {
            return null;
        }

        var segment = (GCSegment*)memory;
        segment->Next = null;
        segment->Start = memory + Align((uint)sizeof(GCSegment));
        segment->End = memory + (pageCount * PageAllocator.PageSize);
        segment->Bump = segment->Start;
        segment->TotalSize = (uint)(segment->End - segment->Start);
        segment->UsedSize = 0;

        return segment;
    }

    /// <summary>
    /// Attempts bump allocation in a pinned segment.
    /// </summary>
    private static void* BumpAllocInPinnedSegment(GCSegment* segment, uint size)
    {
        if (segment == null)
        {
            return null;
        }

        byte* newBump = segment->Bump + size;
        if (newBump <= segment->End)
        {
            void* result = segment->Bump;
            segment->Bump = newBump;
            segment->UsedSize += size;
            return result;
        }

        return null;
    }

    /// <summary>
    /// Appends a pinned segment to the pinned segment list.
    /// </summary>
    private static void AppendPinnedSegment(GCSegment* segment)
    {
        if (segment == null)
        {
            return;
        }

        if (s_pinnedSegments == null)
        {
            s_pinnedSegments = segment;
        }
        else
        {
            GCSegment* tail = s_pinnedSegments;
            while (tail->Next != null)
            {
                tail = tail->Next;
            }

            tail->Next = segment;
        }
    }

    /// <summary>
    /// Checks if a pointer is in the pinned heap.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsInPinnedHeap(nint ptr)
    {
        byte* p = (byte*)ptr;
        GCSegment* segment = s_pinnedSegments;
        while (segment != null)
        {
            if (p >= segment->Start && p < segment->End)
            {
                return true;
            }

            segment = segment->Next;
        }

        return false;
    }

    /// <summary>
    /// Sweeps the pinned heap for unmarked objects.
    /// Pinned objects can still be collected if not referenced.
    /// </summary>
    private static int SweepPinnedHeap()
    {
        int freed = 0;
        GCSegment* segment = s_pinnedSegments;
        while (segment != null)
        {
            freed += SweepPinnedSegment(segment);
            segment = segment->Next;
        }

        return freed;
    }

    /// <summary>
    /// Sweeps a single pinned segment.
    /// </summary>
    private static int SweepPinnedSegment(GCSegment* segment)
    {
        int freed = 0;
        byte* ptr = segment->Start;
        byte* freeRunStart = null;
        uint freeRunSize = 0;

        while (ptr < segment->Bump)
        {
            var obj = (GCObject*)ptr;

            // Validate MethodTable
            MethodTable* mt = obj->GetMethodTable();
            if (mt == null || IsInGCHeap((nint)mt))
            {
                // Skip invalid objects
                ptr += sizeof(nint);
                continue;
            }

            uint objSize = Align(obj->ComputeSize());
            if (objSize == 0 || objSize > (uint)(segment->End - ptr))
            {
                break;
            }

            if (obj->IsMarked)
            {
                // Live pinned object - unmark it
                obj->Unmark();

                // Flush free run
                FlushPinnedFreeRun(freeRunStart, freeRunSize);
                freeRunStart = null;
                freeRunSize = 0;
            }
            else
            {
                // Dead pinned object - add to free run
                freed++;
                if (freeRunStart == null)
                {
                    freeRunStart = ptr;
                }

                freeRunSize += objSize;
            }

            ptr += objSize;
        }

        // Handle trailing free space
        if (freeRunStart != null)
        {
            if (freeRunStart + freeRunSize >= segment->Bump)
            {
                segment->Bump = freeRunStart;
                segment->UsedSize = (uint)(freeRunStart - segment->Start);
            }
            else
            {
                FlushPinnedFreeRun(freeRunStart, freeRunSize);
            }
        }

        return freed;
    }

    /// <summary>
    /// Converts a free run in pinned heap into a FreeBlock.
    /// </summary>
    private static void FlushPinnedFreeRun(byte* start, uint size)
    {
        if (start == null || size < MinBlockSize)
        {
            return;
        }

        var freeBlock = (FreeBlock*)start;
        freeBlock->MethodTable = s_freeMethodTable;
        freeBlock->Size = (int)size;
        freeBlock->Next = null;
    }

    /// <summary>
    /// Reorder pinned segments as FULL -> SEMIFULL -> FREE, and free fully empty multi-page segments.
    /// </summary>
    private static void ReorderPinnedSegmentsAndFreeEmpty()
    {
        GCSegment* fullHead = null;
        GCSegment* fullTail = null;
        GCSegment* semiHead = null;
        GCSegment* semiTail = null;
        GCSegment* freeHead = null;
        GCSegment* freeTail = null;
        GCSegment* seg = s_pinnedSegments;

        while (seg != null)
        {
            GCSegment* next = seg->Next;

            bool isFree = seg->UsedSize == 0 || seg->Bump == seg->Start;
            bool isFull = seg->Bump >= seg->End;

            if (isFree && seg->TotalSize > PageAllocator.PageSize)
            {
                PageAllocator.Free(seg);
            }
            else
            {
                seg->Next = null;

                if (isFull)
                {
                    if (fullHead == null) { fullHead = seg; }
                    else { fullTail->Next = seg; }
                    fullTail = seg;
                }
                else if (isFree)
                {
                    if (freeHead == null) { freeHead = seg; }
                    else { freeTail->Next = seg; }
                    freeTail = seg;
                }
                else
                {
                    if (semiHead == null) { semiHead = seg; }
                    else { semiTail->Next = seg; }
                    semiTail = seg;
                }
            }

            seg = next;
        }

        GCSegment* newHead = null;
        GCSegment* tail = null;

        if (fullHead != null)
        {
            newHead = fullHead;
            tail = fullTail;
        }

        if (semiHead != null)
        {
            if (newHead == null) { newHead = semiHead; }
            else { tail->Next = semiHead; }
            tail = semiTail;
        }

        if (freeHead != null)
        {
            if (newHead == null) { newHead = freeHead; }
            else { tail->Next = freeHead; }
            tail = freeTail;
        }

        s_pinnedSegments = newHead;
        s_currentPinnedSegment = semiHead != null ? semiHead : freeHead;

        s_heapRangeDirty = true;
    }
}
