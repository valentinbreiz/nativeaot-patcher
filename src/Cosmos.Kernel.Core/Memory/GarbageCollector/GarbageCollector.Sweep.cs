// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.CompilerServices;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory.Heap;
using Internal.Runtime;

namespace Cosmos.Kernel.Core.Memory.GarbageCollector;

/// <summary>
/// Sweep phase: segment sweeping, heap sweepers, and heap range helpers.
/// </summary>
public static unsafe partial class GarbageCollector
{
    /// <summary>
    /// Executes the sweep phase across all heap types: GC segments, pinned heap,
    /// small heap, medium heap, and large heap.
    /// </summary>
    /// <returns>Total number of objects freed.</returns>
    private static int SweepPhase()
    {
        int totalFreed = 0;

        GCSegment* segment = s_segments;
        while (segment != null)
        {
            totalFreed += SweepSegment(segment);
            segment = segment->Next;
        }

        totalFreed += SweepPinnedHeap();
        totalFreed += SweepSmallHeap();
        totalFreed += SweepMediumHeap();
        totalFreed += SweepLargeHeap();

        return totalFreed;
    }

    /// <summary>
    /// Sweeps a single GC segment, freeing unmarked objects and coalescing adjacent dead objects
    /// into free blocks. Trailing dead objects reclaim bump pointer space.
    /// </summary>
    /// <param name="segment">The segment to sweep.</param>
    /// <returns>The number of objects freed in this segment.</returns>
    private static int SweepSegment(GCSegment* segment)
    {
        int freed = 0;
        byte* ptr = segment->Start;
        byte* freeRunStart = null;
        uint freeRunSize = 0;

        while (ptr < segment->Bump)
        {
            var obj = (GCObject*)ptr;

            // Get MethodTable (mask off mark bit)
            MethodTable* mt = obj->GetMethodTable();

            if (mt == null)
            {
                // End of valid objects
                break;
            }

            // Check if this is a free block from previous GC
            if (mt == s_freeMethodTable)
            {
                var freeBlock = (FreeBlock*)ptr;
                uint blockSize = (uint)freeBlock->Size;
                if (blockSize == 0 || blockSize > (uint)(segment->End - ptr))
                {
                    break;
                }

                // Accumulate into free run
                if (freeRunStart == null)
                {
                    freeRunStart = ptr;
                }

                freeRunSize += blockSize;

                ptr += blockSize;
                continue;
            }

            // Check if this is a managed object (MT points outside heap to kernel code)
            if (IsInGCHeap((nint)mt))
            {
                // Not a managed object - skip pointer-sized chunk
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
                // Live object - unmark it
                obj->Unmark();

                // Flush accumulated free run as a FreeBlock
                FlushFreeRun(freeRunStart, freeRunSize);
                freeRunStart = null;
                freeRunSize = 0;
            }
            else
            {
                // Dead object - add to free run
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
                FlushFreeRun(freeRunStart, freeRunSize);
            }
        }

        return freed;
    }

    /// <summary>
    /// Converts a contiguous free run into a <see cref="FreeBlock"/> and adds it to the free list.
    /// </summary>
    /// <param name="start">Start of the free run.</param>
    /// <param name="size">Size of the free run in bytes. Must be at least <see cref="MinBlockSize"/>.</param>
    private static void FlushFreeRun(byte* start, uint size)
    {
        if (start == null || size < MinBlockSize)
        {
            return;
        }

        var freeBlock = (FreeBlock*)start;
        freeBlock->MethodTable = s_freeMethodTable;
        freeBlock->Size = (int)size;
        freeBlock->Next = null;
        AddToFreeList(freeBlock);
    }

    /// <summary>
    /// Reorders GC segments (FULL, then SEMI-FULL, then FREE) and releases
    /// fully empty multi-page segments back to the page allocator.
    /// </summary>
    private static void ReorderSegmentsAndFreeEmpty()
    {
        GCSegment* fullHead = null;
        GCSegment* fullTail = null;
        GCSegment* semiHead = null;
        GCSegment* semiTail = null;
        GCSegment* freeHead = null;
        GCSegment* freeTail = null;
        GCSegment* seg = s_segments;

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

        s_segments = newHead;
        s_tailSegment = tail;
        s_lastSegment = semiHead != null ? semiHead : freeHead;
        s_currentSegment = s_lastSegment;

        s_heapRangeDirty = true;
    }

    /// <summary>
    /// Sweeps the small heap (SMT pages) for unmarked objects.
    /// </summary>
    /// <returns>The number of objects freed from the small heap.</returns>
    private static int SweepSmallHeap()
    {
        int freed = 0;
        SMTPage* page = SmallHeap.SMT;

        while (page != null)
        {
            RootSMTBlock* root = page->First;
            while (root != null && root->Size > 0)
            {
                SMTBlock* block = root->First;
                while (block != null)
                {
                    freed += SweepSMTBlock(block, root->Size);
                    block = block->NextBlock;
                }

                root = root->LargerSize;
            }

            page = page->Next;
        }

        return freed;
    }

    /// <summary>
    /// Sweeps a single SMT block, freeing unmarked objects and clearing marks on live ones.
    /// </summary>
    /// <param name="block">The SMT block to sweep.</param>
    /// <param name="itemSize">The allocation item size for this block's size class.</param>
    /// <returns>The number of objects freed in this block.</returns>
    private static int SweepSMTBlock(SMTBlock* block, uint itemSize)
    {
        int freed = 0;
        ulong elementSize = itemSize + SmallHeap.PrefixBytes;
        ulong positions = PageAllocator.PageSize / elementSize;

        for (ulong i = 0; i < positions; i++)
        {
            byte* slotPtr = block->PagePtr + i * elementSize;
            ushort* header = (ushort*)slotPtr;

            ushort size = header[0];
            if (size == 0)
            {
                continue; // Not allocated
            }

            // Get object pointer
            byte* objPtr = slotPtr + SmallHeap.PrefixBytes;
            var obj = (GCObject*)objPtr;

            // Validate MethodTable - must point outside heap (to kernel code)
            nuint mtPtr = (nuint)obj->MethodTable & ~(nuint)1;
            if (mtPtr == 0 || !IsInGCHeap((nint)mtPtr))
            {
                continue;
            }

            if (!obj->IsMarked)
            {
                // Unmarked - free the object
                SmallHeap.Free(objPtr);
                freed++;
            }
            else
            {
                // Marked - clear the mark for next cycle
                obj->Unmark();
            }
        }

        return freed;
    }

    /// <summary>
    /// Sweeps the medium heap by scanning all HeapMedium pages in the RAT.
    /// </summary>
    /// <returns>The number of objects freed from the medium heap.</returns>
    private static int SweepMediumHeap()
    {
        int freed = 0;

        // Iterate through RAT looking for HeapMedium pages
        byte* ramStart = PageAllocator.RamStart;
        for (ulong pageIdx = 0; pageIdx < PageAllocator.TotalPageCount; pageIdx++)
        {
            PageType type = PageAllocator.GetPageType(ramStart + pageIdx * PageAllocator.PageSize);
            if (type != PageType.HeapMedium)
            {
                continue;
            }

            byte* pagePtr = ramStart + pageIdx * PageAllocator.PageSize;
            var header = (MediumHeapHeader*)pagePtr;

            if (header->Size == 0)
            {
                continue; // Not allocated
            }

            byte* objPtr = pagePtr + MediumHeap.PrefixBytes;
            var obj = (GCObject*)objPtr;

            // Validate MethodTable - must point outside heap (to kernel code)
            nuint mtPtr = (nuint)obj->MethodTable & ~(nuint)1;
            if (mtPtr == 0 || !IsInGCHeap((nint)mtPtr))
            {
                continue;
            }

            if (!obj->IsMarked)
            {
                // Unmarked - free the object
                MediumHeap.Free(objPtr);
                freed++;
            }
            else
            {
                // Marked - clear the mark for next cycle
                obj->Unmark();
            }
        }

        return freed;
    }

    /// <summary>
    /// Sweeps the large heap by scanning all HeapLarge pages in the RAT.
    /// </summary>
    /// <returns>The number of objects freed from the large heap.</returns>
    private static int SweepLargeHeap()
    {
        int freed = 0;

        // Iterate through RAT looking for HeapLarge pages
        byte* ramStart = PageAllocator.RamStart;
        for (ulong pageIdx = 0; pageIdx < PageAllocator.TotalPageCount; pageIdx++)
        {
            PageType type = PageAllocator.GetPageType(ramStart + pageIdx * PageAllocator.PageSize);
            if (type != PageType.HeapLarge)
            {
                continue;
            }

            byte* pagePtr = ramStart + pageIdx * PageAllocator.PageSize;
            var header = (LargeHeapHeader*)pagePtr;

            if (header->Size == 0)
            {
                continue; // Not allocated
            }

            byte* objPtr = pagePtr + LargeHeap.PrefixBytes;
            var obj = (GCObject*)objPtr;

            // Validate MethodTable - must point outside heap (to kernel code)
            nuint mtPtr = (nuint)obj->MethodTable & ~(nuint)1;
            if (mtPtr == 0 || !IsInGCHeap((nint)mtPtr))
            {
                continue;
            }

            if (!obj->IsMarked)
            {
                // Unmarked - free the object
                LargeHeap.Free(objPtr);
                freed++;
            }
            else
            {
                // Marked - clear the mark for next cycle
                obj->Unmark();
            }
        }

        return freed;
    }

    // --- Helpers ---

    /// <summary>
    /// Checks if a pointer falls within any GC heap segment (including pinned segments).
    /// Uses a cached min/max range for a fast pre-check before walking the segment list.
    /// </summary>
    /// <param name="ptr">The pointer to test.</param>
    /// <returns><c>true</c> if <paramref name="ptr"/> is inside a GC or pinned heap segment; otherwise, <c>false</c>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsInGCHeap(nint ptr)
    {
        if (s_heapRangeDirty)
        {
            RecomputeHeapRange();
        }

        byte* p = (byte*)ptr;
        if (p < s_gcHeapMin || p >= s_gcHeapMax)
        {
            // Check pinned heap
            return IsInPinnedHeap(ptr);
        }

        GCSegment* segment = s_segments;
        while (segment != null)
        {
            if (p >= segment->Start && p < segment->End)
            {
                return true;
            }

            segment = segment->Next;
        }

        return IsInPinnedHeap(ptr);
    }

    /// <summary>
    /// Recomputes the cached heap min/max range from the current segment list.
    /// </summary>
    private static void RecomputeHeapRange()
    {
        if (s_segments == null)
        {
            s_gcHeapMin = (byte*)0;
            s_gcHeapMax = (byte*)0;
            s_heapRangeDirty = false;
            return;
        }

        byte* min = s_segments->Start;
        byte* max = s_segments->End;

        for (GCSegment* seg = s_segments->Next; seg != null; seg = seg->Next)
        {
            if (seg->Start < min)
            {
                min = seg->Start;
            }

            if (seg->End > max)
            {
                max = seg->End;
            }
        }

        s_gcHeapMin = min;
        s_gcHeapMax = max;
        s_heapRangeDirty = false;
    }
}
