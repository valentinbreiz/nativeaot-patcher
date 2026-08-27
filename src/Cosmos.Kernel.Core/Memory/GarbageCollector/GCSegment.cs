// This code is licensed under the BSD 3-Clause license (see LICENSE for details)

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Cosmos.Kernel.Core.Memory.GarbageCollector;

/// <summary>
/// Describes a contiguous GC heap segment used for bump allocation.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct GCSegment
{
    /// <summary>
    /// Number of pointer-aligned slots covered by a single brick-table byte.
    /// </summary>
    /// <remarks>
    /// Each brick-table entry stores a 1-based index of the last object slot in its chunk. A value of 0
    /// means that no object has been recorded for that chunk.
    /// </remarks>
    internal const int SlotsPerChunk = 255;

    /// <summary>
    /// Pointer to the next segment in the linked list of heap segments.
    /// </summary>
    public GCSegment* Next;

    /// <summary>
    /// Start of the segment (after the segment's brick table).
    /// </summary>
    public byte* Start;

    /// <summary>
    /// End of the segment's address range.
    /// </summary>
    public byte* End;

    /// <summary>
    /// Current bump allocation pointer. Advances toward <see cref="End"/>.
    /// </summary>
    public byte* Bump;

    /// <summary>
    /// Total usable size in bytes (<see cref="End"/> - <see cref="Start"/>).
    /// </summary>
    public uint TotalSize;

    /// <summary>
    /// Bytes currently in use (live + dead objects before sweep).
    /// </summary>
    public uint UsedSize;

    /// <summary>
    /// Returns a span over the brick-table bytes that immediately follow this struct in memory.
    /// </summary>
    /// <returns>A span covering the segment's brick table.</returns>
    public readonly Span<byte> GetBrickTable()
    {
        void* aThis = Unsafe.AsPointer(ref Unsafe.AsRef(in this));
        byte* bricktable = (byte*)aThis + (uint)sizeof(GCSegment);
        return new Span<byte>(bricktable, (int)(Start - bricktable));
    }

    /// <summary>
    /// Records the slot containing the specified address as the last object in its brick-table chunk.
    /// </summary>
    /// <param name="addr">The address of an object start within this segment.</param>
    public void MarkObject(IntPtr addr)
    {
        nint slotIndex = (addr - (IntPtr)Start) / IntPtr.Size;
        int chunkIndex = (int)(slotIndex / SlotsPerChunk);
        byte posInChunk = (byte)(slotIndex % SlotsPerChunk + 1);

        var brickTable = GetBrickTable();

        if (brickTable[chunkIndex] == 0 || posInChunk > brickTable[chunkIndex])
        {
            brickTable[chunkIndex] = posInChunk;
        }
    }

    /// <summary>
    /// Finds the start address of the closest object recorded at or below the supplied address.
    /// </summary>
    /// <param name="addr">The address to search below.</param>
    /// <returns>
    /// The start address of the closest known object, or <see cref="Start"/> when no earlier object is recorded.
    /// </returns>
    public readonly IntPtr FindClosestObjectBelow(IntPtr addr)
    {
        nint slotIndex = (addr - (IntPtr)Start) / IntPtr.Size;
        int chunkIndex = (int)(slotIndex / SlotsPerChunk);
        nint posInChunk = slotIndex % SlotsPerChunk;

        var brickTable = GetBrickTable();

        if (brickTable[chunkIndex] != 0 && brickTable[chunkIndex] - 1 <= posInChunk)
        {
            return (nint)(Start + (chunkIndex * SlotsPerChunk + (brickTable[chunkIndex] - 1)) * IntPtr.Size);
        }

        int i = brickTable[..chunkIndex].LastIndexOfAnyExcept((byte)0);

        if (i >= 0)
        {
            return (nint)(Start + (i * SlotsPerChunk + (brickTable[i] - 1)) * IntPtr.Size);
        }

        return (nint)Start;
    }

    internal ref struct Enumerator
    {
        private readonly byte* _start;
        private readonly byte* _end;

        public GCObject* Current { get; private set; }

        public Enumerator(byte* start, byte* end)
        {
            _start = start;
            _end = end;
        }

        public bool MoveNext()
        {
            if (Current == null)
            {
                Current = (GCObject*)_start;
                return Current < (GCObject*)_end;
            }

            uint size = Current->ComputeSize();

            if (Current + size >= (GCObject*)_end)
            {
                Current = null;
                return false;
            }


            Current = (GCObject*)((byte*)Current + size);
            return true;
        }
    }
}
