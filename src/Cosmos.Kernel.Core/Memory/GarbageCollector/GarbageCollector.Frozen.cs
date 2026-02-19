// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.Core.IO;
using Internal.Runtime;

namespace Cosmos.Kernel.Core.Memory.GarbageCollector;

public static unsafe partial class GarbageCollector
{
    #region Frozen Segments

    /// <summary>
    /// Information about a frozen segment.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct FrozenSegmentInfo
    {
        public nint Start;
        public nuint AllocSize;
        public nuint CommitSize;
        public nuint ReservedSize;
        public FrozenSegmentInfo* Next;
    }

    // Linked list of frozen segments
    private static FrozenSegmentInfo* _frozenSegments;

    // Metadata storage for frozen segment info
    private static byte* _frozenSegmentMetadataPage;
    private static int _frozenSegmentMetadataOffset;

    /// <summary>
    /// Checks if a pointer is within a frozen segment.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsInFrozenSegment(nint ptr)
    {
        FrozenSegmentInfo* segment = _frozenSegments;
        while (segment != null)
        {
            if (ptr >= segment->Start && ptr < segment->Start + (nint)segment->AllocSize)
                return true;
            segment = segment->Next;
        }
        return false;
    }

    /// <summary>
    /// Called by runtime to register frozen segments (preinitialized data).
    /// Frozen segments contain pre-initialized objects that should never be collected.
    /// </summary>
    internal static nint RegisterFrozenSegment(nint pSegmentStart, nuint allocSize, nuint commitSize, nuint reservedSize)
    {
        // Allocate info structure from unmanaged memory (frozen segments are pre-allocated)
        // Use a simple bump allocator for metadata
        int InfoSize = sizeof(FrozenSegmentInfo);
        const int PageSize = (int)PageAllocator.PageSize;

        // Allocate a page if we don't have one, or use existing
        if (_frozenSegmentMetadataPage == null || _frozenSegmentMetadataOffset + InfoSize > PageSize)
        {
            _frozenSegmentMetadataPage = (byte*)PageAllocator.AllocPages(PageType.Unmanaged, 1, true);
            if (_frozenSegmentMetadataPage == null)
            {
                Serial.WriteString("[GC] ERROR: Failed to allocate frozen segment metadata page\n");
                return pSegmentStart;
            }
            _frozenSegmentMetadataOffset = 0;
        }

        FrozenSegmentInfo* info = (FrozenSegmentInfo*)(_frozenSegmentMetadataPage + _frozenSegmentMetadataOffset);
        _frozenSegmentMetadataOffset += InfoSize;

        info->Start = pSegmentStart;
        info->AllocSize = allocSize;
        info->CommitSize = commitSize;
        info->ReservedSize = reservedSize;
        info->Next = _frozenSegments;

        _frozenSegments = info;

        Serial.WriteString("[GC] Registered frozen segment at 0x");
        Serial.WriteHex((ulong)pSegmentStart);
        Serial.WriteString(", size: ");
        Serial.WriteNumber((uint)allocSize);
        Serial.WriteString("\n");

        return pSegmentStart;
    }

    /// <summary>
    /// Called by runtime to update frozen segment.
    /// </summary>
    internal static void UpdateFrozenSegment(nint seg, nint allocated, nint committed)
    {
        // Find the segment and update its committed size
        FrozenSegmentInfo* segment = _frozenSegments;
        while (segment != null)
        {
            if (segment->Start == seg)
            {
                segment->CommitSize = (nuint)committed;
                segment->AllocSize = (nuint)allocated;
                break;
            }
            segment = segment->Next;
        }
    }
    #endregion
}