// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.IO;

namespace Cosmos.Kernel.Core.Memory.GarbageCollector;

public static unsafe partial class GarbageCollector
{
    // --- Nested types ---

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

    // --- Static fields ---

    // Linked list of frozen segments
    private static FrozenSegmentInfo* s_frozenSegments;

    // Metadata storage for frozen segment info
    private static byte* s_frozenSegmentMetadataPage;
    private static int s_frozenSegmentMetadataOffset;

    // --- Methods ---

    /// <summary>
    /// Checks if a pointer is within a frozen segment.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsInFrozenSegment(nint ptr)
    {
        FrozenSegmentInfo* segment = s_frozenSegments;
        while (segment != null)
        {
            if (ptr >= segment->Start && ptr < segment->Start + (nint)segment->AllocSize)
            {
                return true;
            }

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
        int infoSize = sizeof(FrozenSegmentInfo);
        const int pageSize = (int)PageAllocator.PageSize;

        // Allocate a page if we don't have one, or use existing
        if (s_frozenSegmentMetadataPage == null || s_frozenSegmentMetadataOffset + infoSize > pageSize)
        {
            s_frozenSegmentMetadataPage = (byte*)PageAllocator.AllocPages(PageType.Unmanaged, 1, true);
            if (s_frozenSegmentMetadataPage == null)
            {
                Serial.WriteString("[GC] ERROR: Failed to allocate frozen segment metadata page\n");
                return pSegmentStart;
            }

            s_frozenSegmentMetadataOffset = 0;
        }

        var info = (FrozenSegmentInfo*)(s_frozenSegmentMetadataPage + s_frozenSegmentMetadataOffset);
        s_frozenSegmentMetadataOffset += infoSize;

        info->Start = pSegmentStart;
        info->AllocSize = allocSize;
        info->CommitSize = commitSize;
        info->ReservedSize = reservedSize;
        info->Next = s_frozenSegments;

        s_frozenSegments = info;

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
        FrozenSegmentInfo* segment = s_frozenSegments;
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
}
