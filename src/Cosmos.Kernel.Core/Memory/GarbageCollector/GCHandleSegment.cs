// This code is licensed under the BSD 3-Clause license (see LICENSE for details)

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.IO;

namespace Cosmos.Kernel.Core.Memory.GarbageCollector;

/// <summary>
/// Represents a segment of GC handles in the handle store.
/// </summary>
internal unsafe struct GCHandleSegment
{
    /// <summary>
    /// Number of GCHandles a single segment can hold.
    /// </summary>
    /// <remarks>
    /// This value is 170 as it is the result of (<see cref="PageAllocator.PageSize"/> - sizeof(<see cref="GCHandleSegment"/>)) / sizeof(<see cref="GCHandle"/>),
    /// or just (4096 - 16) / 24.
    /// </remarks>
    public static int Capacity = ((int)PageAllocator.PageSize - sizeof(GCHandleSegment)) / sizeof(GCHandle);

    /// <summary>
    /// Link to the next segment in the handle-store chain.
    /// </summary>
    public GCHandleSegment* Next;

    /// <summary>
    /// Packed state for the segment's free-list head, live-handle count, and a version tag used to detect ABA races.
    /// </summary>
    /// <remarks>
    /// The packed value uses the following layout:
    /// Bits 0-15: head of the free list (index of the first free handle)
    /// Bits 16-31: number of currently alive handles in the segment
    /// Bits 32-63: version tag used to avoid ABA problems.
    /// </remarks>
    private ulong _freeHead;

    /// <summary>
    /// Pointer to the first handle slot in this segment.
    /// </summary>
    public GCHandle* Start => (GCHandle*)((IntPtr)Unsafe.AsPointer(ref this) + sizeof(GCHandleSegment));

    /// <summary>
    /// Gets the number of live handles currently tracked by this segment.
    /// </summary>
    public int AliveCount => Unpack(Volatile.Read(ref _freeHead)).Alive;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Pack(ushort head, ushort alive, uint tag)
    {
        return ((ulong)tag << 32) | ((ulong)head << 16) | alive;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (ushort Head, ushort Alive, uint Tag) Unpack(ulong packed)
    {
        return ((ushort)(packed >> 16), (ushort)packed, (uint)(packed >> 32));
    }

    /// <summary>
    /// Determines whether the provided handle pointer belongs to this segment.
    /// </summary>
    /// <param name="handle">The handle pointer to test.</param>
    /// <returns><see langword="true"/> if the pointer falls within this segment's range; otherwise, <see langword="false"/>.</returns>
    public bool ContainsHandle(GCHandle* handle)
    {
        GCHandle* start = Start;
        return handle >= start && handle < start + Capacity;
    }

    /// <summary>
    /// Tries to allocate a slot from this segment's free list.
    /// </summary>
    /// <returns>The allocated handle pointer, or <see langword="null"/> if the segment is full.</returns>
    public GCHandle* TryAllocate()
    {
        while (true)
        {
            var packed = Volatile.Read(ref _freeHead);
            var (head, alive, tag) = Unpack(packed);

            if (head == ushort.MaxValue)
            {
                // Segment is full
                return null;
            }

            var slot = Start + head;
            var next = (ushort)slot->ExtraInfo;
            var newPacked = Pack(next, (ushort)(alive + 1), tag + 1);

            // Make sure the spot wasn't taken by another thread.
            if (Interlocked.CompareExchange(ref _freeHead, newPacked, packed) == packed)
            {
                slot->ExtraInfo = 0;
                slot->Object = null;

                return slot;
            }
        }
    }

    /// <summary>
    /// Returns a handle slot to this segment's free list.
    /// </summary>
    /// <param name="handle">The handle slot to release.</param>
    public void Free(GCHandle* handle)
    {
        handle->Object = null;
        handle->Type = GCHandle.FreeHandleType;

        ushort index = (ushort)(handle - Start);

        while (true)
        {
            ulong packed = Volatile.Read(ref _freeHead);

            var (head, alive, tag) = Unpack(packed);

            handle->ExtraInfo = head;

            ulong newPacked = Pack(index, (ushort)(alive - 1), tag + 1);

            // Make sure the spot wasn't taken by another thread.
            if (Interlocked.CompareExchange(ref _freeHead, newPacked, packed) == packed)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Initializes the segment and setup free list.
    /// </summary>
    internal void Initialize()
    {
        _freeHead = Pack(0, 0, 0);

        var buffer = Start;
        for (int i = 0; i < Capacity; i++)
        {
            buffer[i].Type = GCHandle.FreeHandleType;
            buffer[i].ExtraInfo = i + 1;
        }

        buffer[Capacity - 1].ExtraInfo = ushort.MaxValue; // End of free list
    }
}
