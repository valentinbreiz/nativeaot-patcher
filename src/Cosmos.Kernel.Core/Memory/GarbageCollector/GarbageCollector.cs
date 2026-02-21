// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.Core.IO;
using Internal.Runtime;

namespace Cosmos.Kernel.Core.Memory.GarbageCollector;

#pragma warning disable CS8500

/// <summary>
/// Mark-and-sweep garbage collector with free list allocation.
/// Manages GC heap segments, pinned heap, frozen segments, and GC handles.
/// </summary>
public static unsafe partial class GarbageCollector
{
    // --- Nested types ---

    /// <summary>
    /// Represents a free block in the GC heap, linked into size-class free lists.
    /// Laid out to be walkable like a <see cref="GCObject"/> (MethodTable at offset 0, Size at offset 8).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct FreeBlock
    {
        /// <summary>
        /// Points to <see cref="s_freeMethodTable"/> to identify this block as free during heap walks.
        /// </summary>
        public MethodTable* MethodTable;

        /// <summary>
        /// Total size of this free block in bytes (occupies the same position as <see cref="GCObject.Length"/>).
        /// </summary>
        public int Size;

        /// <summary>
        /// Next free block in this size class bucket.
        /// </summary>
        public FreeBlock* Next;
    }

    /// <summary>
    /// Marker type whose MethodTable is used to tag free blocks in the heap.
    /// </summary>
    internal struct FreeMarker { }

    /// <summary>
    /// Describes a contiguous GC heap segment used for bump allocation.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct GCSegment
    {
        /// <summary>
        /// Next segment in the linked list.
        /// </summary>
        public GCSegment* Next;

        /// <summary>
        /// Start of the usable allocation area (after the segment header).
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
    }

    /// <summary>
    /// Describes a value-type series within a GCDesc for arrays of structs containing references.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct ValSerieItem
    {
        /// <summary>
        /// Number of pointer-sized reference fields in this series.
        /// </summary>
        public uint Nptrs;

        /// <summary>
        /// Number of bytes to skip after the reference fields.
        /// </summary>
        public uint Skip;
    }

    /// <summary>
    /// Describes a reference series within a GCDesc stored before the MethodTable.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GCDescSeries
    {
        /// <summary>
        /// Size of the series relative to the object size. Added to object size to get byte count.
        /// </summary>
        public nint SeriesSize;

        /// <summary>
        /// Byte offset from the object base where this series begins.
        /// </summary>
        public nint StartOffset;
    }

    // --- Constants ---

    /// <summary>
    /// Number of free list size classes (powers of two: 16, 32, 64, ... 32768).
    /// </summary>
    private const int NumSizeClasses = 12;

    /// <summary>
    /// Smallest free list size class in bytes.
    /// </summary>
    private const uint MinSizeClass = 16;

    /// <summary>
    /// Minimum block size in bytes. Must be large enough to hold a <see cref="FreeBlock"/> header (24 bytes on x64).
    /// </summary>
    private const uint MinBlockSize = 24;

    // --- Static fields ---

    /// <summary>
    /// Array of free list heads, indexed by size class.
    /// </summary>
    private static FreeBlock** s_freeLists;

    /// <summary>
    /// Whether the free list array has been allocated and initialized.
    /// </summary>
    private static bool s_freeListsInitialized;

    /// <summary>
    /// MethodTable pointer used to tag <see cref="FreeBlock"/> entries in the heap.
    /// </summary>
    private static MethodTable* s_freeMethodTable;

    /// <summary>
    /// Head of the GC segment linked list.
    /// </summary>
    private static GCSegment* s_segments;

    /// <summary>
    /// Segment currently being used for bump allocation.
    /// </summary>
    private static GCSegment* s_currentSegment;

    /// <summary>
    /// Last segment where allocation succeeded (used as a fast-path hint).
    /// </summary>
    private static GCSegment* s_lastSegment;

    /// <summary>
    /// Tail of the segment linked list (for O(1) append).
    /// </summary>
    private static GCSegment* s_tailSegment;

    /// <summary>
    /// Default segment size. Grows as needed.
    /// </summary>
    private static uint s_maxSegmentSize = (uint)PageAllocator.PageSize;

    /// <summary>
    /// Lowest address across all GC segments (for fast heap range pre-check).
    /// </summary>
    private static byte* s_gcHeapMin;

    /// <summary>
    /// Highest address across all GC segments (for fast heap range pre-check).
    /// </summary>
    private static byte* s_gcHeapMax;

    /// <summary>
    /// Set to <c>true</c> when segments are added or removed, triggering a range recomputation.
    /// </summary>
    private static bool s_heapRangeDirty;

    /// <summary>
    /// Stack used during the mark phase for iterative object traversal.
    /// </summary>
    private static nint* s_markStack;

    /// <summary>
    /// Maximum number of entries the mark stack can hold.
    /// </summary>
    private static int s_markStackCapacity;

    /// <summary>
    /// Current number of entries in the mark stack.
    /// </summary>
    private static int s_markStackCount;

    /// <summary>
    /// Number of pages currently backing the mark stack.
    /// </summary>
    private static ulong s_markStackPageCount = 1;

    /// <summary>
    /// Whether the GC has been initialized.
    /// </summary>
    private static bool s_initialized = false;

    /// <summary>
    /// Total number of collections performed since initialization.
    /// </summary>
    private static int s_totalCollections;

    /// <summary>
    /// Cumulative number of objects freed across all collections.
    /// </summary>
    private static int s_totalObjectsFreed;

    // --- Properties ---

    /// <summary>
    /// Gets a value indicating whether the garbage collector is enabled.
    /// </summary>
    public static bool IsEnabled
    {
        get
        {
            return s_initialized;
        }
    }

    // --- Public methods ---

    /// <summary>
    /// Initializes the garbage collector, allocating the free list array, initial segment, and mark stack.
    /// </summary>
    public static void Initialize()
    {
        if (s_initialized)
        {
            return;
        }

        Serial.WriteString("[GC] Initializing with free list allocator\n");

        // Allocate free list array using page allocator (not GC heap)
        s_freeLists = (FreeBlock**)PageAllocator.AllocPages(PageType.Unmanaged, 1, true);
        if (s_freeLists == null)
        {
            Serial.WriteString("[GC] ERROR: Failed to allocate free lists\n");
            return;
        }

        for (int i = 0; i < NumSizeClasses; i++)
        {
            s_freeLists[i] = null;
        }

        s_freeListsInitialized = true;

        // Get the free marker MethodTable
        s_freeMethodTable = MethodTable.Of<FreeMarker>();

        // Allocate initial segment
        s_currentSegment = AllocateSegment(s_maxSegmentSize);
        s_segments = s_currentSegment;
        s_lastSegment = s_currentSegment;
        s_tailSegment = s_currentSegment;
        s_heapRangeDirty = true;
        RecomputeHeapRange();
        if (s_segments == null)
        {
            Serial.WriteString("[GC] ERROR: Failed to allocate initial segment\n");
            return;
        }

        // Allocate mark stack
        s_markStackCapacity = 4096;
        s_markStack = (nint*)PageAllocator.AllocPages(PageType.Unmanaged, 1, true);
        if (s_markStack == null)
        {
            Serial.WriteString("[GC] ERROR: Failed to allocate mark stack\n");
            return;
        }

        s_markStackCount = 0;

        InitializeGCHandleStore();

        s_initialized = true;
        Serial.WriteString("[GC] Initialization complete\n");
    }

    /// <summary>
    /// Performs a full garbage collection: mark, sweep, and segment reordering.
    /// </summary>
    /// <returns>The number of objects freed during this collection.</returns>
    public static int Collect()
    {
        if (!s_initialized)
        {
            return 0;
        }

        int freedCount;
        using (InternalCpu.DisableInterruptsScope())
        {
            Serial.WriteString("[GC] Collection #");
            Serial.WriteNumber((uint)s_totalCollections + 1);
            Serial.WriteString("\n");

            // Clear free lists - will be rebuilt during sweep
            for (int i = 0; i < NumSizeClasses; i++)
            {
                s_freeLists[i] = null;
            }

            // Mark reachable objects
            MarkPhase();

            // Free Weak GC Handles
            FreeWeakHandles();

            // Sweep and rebuild free lists
            freedCount = SweepPhase();

            // Reorder segments and free empty ones
            ReorderSegmentsAndFreeEmpty();
            ReorderPinnedSegmentsAndFreeEmpty();
            RecomputeHeapRange();

            s_totalCollections++;
            s_totalObjectsFreed += freedCount;

            Serial.WriteString("[GC] Freed ");
            Serial.WriteNumber((uint)freedCount);
            Serial.WriteString(" objects\n");
        }

        return freedCount;
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

    // --- Internal methods ---

    /// <summary>
    /// Allocates memory for a managed object. Called by the runtime allocation helpers.
    /// Tries free list, then bump allocation, then triggers a collection as a last resort.
    /// </summary>
    /// <param name="size">Requested object size in bytes.</param>
    /// <param name="flags">Runtime allocation flags (e.g., pinned object heap).</param>
    /// <returns>Pointer to the allocated object, or <c>null</c> if allocation fails.</returns>
    internal static GCObject* AllocObject(nint size, GC_ALLOC_FLAGS flags)
    {
        if (!s_initialized)
        {
            Initialize();
        }

        // Check for pinned object allocation
        if ((flags & GC_ALLOC_FLAGS.GC_ALLOC_PINNED_OBJECT_HEAP) != 0)
        {
            return AllocPinnedObject(size, flags);
        }

        uint allocSize = Align((uint)size);
        if (allocSize < MinBlockSize)
        {
            allocSize = MinBlockSize;
        }

        // Try free list allocation first
        void* result = AllocFromFreeList(allocSize);
        if (result != null)
        {
            return (GCObject*)result;
        }

        // Try fast bump allocation from last segment
        result = BumpAllocInSegment(s_lastSegment, allocSize);
        if (result != null)
        {
            return (GCObject*)result;
        }

        // Slow path: walk segments from s_lastSegment and append if needed
        result = AllocateObjectSlow(allocSize);
        if (result != null)
        {
            return (GCObject*)result;
        }

        // Last resort: collect and retry
        Collect();

        result = AllocFromFreeList(allocSize);
        if (result != null)
        {
            return (GCObject*)result;
        }

        result = AllocateObjectSlow(allocSize);
        return (GCObject*)result;
    }
}
