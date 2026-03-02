// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.CompilerServices;
using Cosmos.Kernel.Core.IO;

namespace Cosmos.Kernel.Core.Memory.GarbageCollector;

/// <summary>
/// Allocation methods: segment management, bump allocation, and free list operations.
/// </summary>
public static unsafe partial class GarbageCollector
{
    /// <summary>
    /// Allocates a new GC segment backed by page-allocated memory.
    /// </summary>
    /// <param name="requestedSize">Minimum usable size in bytes.</param>
    /// <returns>Pointer to the initialized segment, or <c>null</c> if page allocation fails.</returns>
    private static GCSegment* AllocateSegment(uint requestedSize)
    {
        uint size = requestedSize < s_maxSegmentSize ? s_maxSegmentSize : requestedSize;
        uint totalSize = size + (uint)sizeof(GCSegment);
        ulong pageCount = (totalSize + PageAllocator.PageSize - 1) / PageAllocator.PageSize;

        var memory = (byte*)PageAllocator.AllocPages(PageType.GCHeap, pageCount, true);
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
    /// Aligns a size up to the nearest pointer-sized boundary.
    /// </summary>
    /// <param name="size">The size to align.</param>
    /// <returns>The aligned size.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Align(uint size)
    {
        return (size + ((uint)sizeof(nint) - 1)) & ~((uint)sizeof(nint) - 1);
    }

    /// <summary>
    /// Searches the free lists for a block of the requested size.
    /// Splits oversized blocks and returns the remainder to the free list.
    /// </summary>
    /// <param name="size">Aligned allocation size in bytes.</param>
    /// <returns>Pointer to zeroed memory, or <c>null</c> if no suitable block is found.</returns>
    private static void* AllocFromFreeList(uint size)
    {
        if (!s_freeListsInitialized)
        {
            return null;
        }

        int sizeClass = -1;
        uint classSize = MinSizeClass;
        for (int i = 0; i < NumSizeClasses; i++, classSize <<= 1)
        {
            if (size <= classSize)
            {
                sizeClass = i;
                break;
            }
        }

        if (sizeClass < 0)
        {
            return null; // Too large
        }

        // Try this size class and larger
        for (int i = sizeClass; i < NumSizeClasses; i++)
        {
            FreeBlock* block = s_freeLists[i];
            if (block == null)
            {
                continue;
            }

            // Check each block in this class
            FreeBlock* prev = null;
            while (block != null)
            {
                if (block->Size >= size)
                {
                    uint remainder = (uint)(block->Size - size);

                    // Avoid unsplittable tail: skip this block if it would leave a tiny remainder
                    if (remainder != 0 && remainder < MinBlockSize)
                    {
                        prev = block;
                        block = block->Next;
                        continue;
                    }

                    // Remove from free list
                    if (prev != null)
                    {
                        prev->Next = block->Next;
                    }
                    else
                    {
                        s_freeLists[i] = block->Next;
                    }

                    // Split if remainder is usable
                    if (remainder >= MinBlockSize)
                    {
                        var split = (FreeBlock*)((byte*)block + size);
                        split->MethodTable = s_freeMethodTable;
                        split->Size = (int)remainder;
                        split->Next = null;
                        AddToFreeList(split);
                    }

                    // Clear and return
                    MemoryOp.MemSet((byte*)block, 0, (int)size);
                    return block;
                }

                prev = block;
                block = block->Next;
            }
        }

        return null;
    }

    /// <summary>
    /// Attempts bump allocation within a specific segment.
    /// </summary>
    /// <param name="segment">The segment to allocate from.</param>
    /// <param name="size">Number of bytes to allocate.</param>
    /// <returns>Pointer to the allocated memory, or <c>null</c> if the segment has insufficient space.</returns>
    private static void* BumpAllocInSegment(GCSegment* segment, uint size)
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
            s_currentSegment = segment;
            s_lastSegment = segment;
            return result;
        }

        return null;
    }

    /// <summary>
    /// Slow allocation path: walks all segments looking for space, then allocates a new segment if needed.
    /// </summary>
    /// <param name="size">Number of bytes to allocate.</param>
    /// <returns>Pointer to the allocated memory, or <c>null</c> if allocation fails.</returns>
    private static void* AllocateObjectSlow(uint size)
    {
        if (s_segments == null)
        {
            return null;
        }

        if (s_lastSegment == null)
        {
            s_lastSegment = s_segments;
        }

        GCSegment* start = s_lastSegment;

        // Pass 1: from s_lastSegment to end
        for (GCSegment* seg = start; seg != null; seg = seg->Next)
        {
            void* result = BumpAllocInSegment(seg, size);
            if (result != null)
            {
                return result;
            }
        }

        // Pass 2: from head to s_lastSegment (exclusive)
        for (GCSegment* seg = s_segments; seg != start; seg = seg->Next)
        {
            void* result = BumpAllocInSegment(seg, size);
            if (result != null)
            {
                return result;
            }
        }

        // No segment fits: allocate and append a new segment at tail
        GCSegment* newSegment = AllocateSegment(size);
        if (newSegment == null)
        {
            return null;
        }

        AppendSegment(newSegment);
        s_lastSegment = newSegment;
        s_currentSegment = newSegment;

        return BumpAllocInSegment(newSegment, size);
    }

    /// <summary>
    /// Appends a segment to the end of the GC segment linked list.
    /// </summary>
    /// <param name="segment">The segment to append.</param>
    private static void AppendSegment(GCSegment* segment)
    {
        if (segment == null)
        {
            return;
        }

        segment->Next = null;

        if (s_segments == null)
        {
            s_segments = segment;
            s_tailSegment = segment;
            s_heapRangeDirty = true;
            return;
        }

        s_tailSegment->Next = segment;
        s_tailSegment = segment;
        s_heapRangeDirty = true;
    }

    /// <summary>
    /// Inserts a free block into the appropriate size-class free list.
    /// </summary>
    /// <param name="block">The free block to add.</param>
    private static void AddToFreeList(FreeBlock* block)
    {
        if (!s_freeListsInitialized || block == null || block->Size < MinBlockSize)
        {
            return;
        }

        block->MethodTable = s_freeMethodTable;

        int sizeClass = -1;
        uint classSize = MinSizeClass;
        uint size = (uint)block->Size;
        for (int i = 0; i < NumSizeClasses; i++, classSize <<= 1)
        {
            if (size <= classSize)
            {
                sizeClass = i;
                break;
            }
        }

        if (sizeClass < 0)
        {
            sizeClass = NumSizeClasses - 1;
        }

        block->Next = s_freeLists[sizeClass];
        s_freeLists[sizeClass] = block;
    }
}
