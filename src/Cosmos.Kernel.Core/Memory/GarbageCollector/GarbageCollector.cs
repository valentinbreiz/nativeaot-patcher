// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory.Heap;
using Cosmos.Kernel.Core.Runtime;
using Cosmos.Kernel.Core.Scheduler;
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
    private static bool s_initialized;

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
    /// <value>Always <c>true</c> for this implementation.</value>
    public static bool IsEnabled
    {
        get
        {
            return true;
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

    // --- Private methods: Allocation ---

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

        if (s_tailSegment != null)
        {
            s_tailSegment->Next = segment;
            s_tailSegment = segment;
            s_heapRangeDirty = true;
            return;
        }

        // Fallback if tail is not tracked
        GCSegment* tail = s_segments;
        while (tail->Next != null)
        {
            tail = tail->Next;
        }

        tail->Next = segment;
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

    #region Mark Phase

    /// <summary>
    /// Executes the mark phase: scans roots (stack, GC handles) and marks all reachable objects.
    /// </summary>
    private static void MarkPhase()
    {
        s_markStackCount = 0;
        ScanStackRoots();
        ScanGCHandles();
        //ScanStaticRoots();
    }

    /// <summary>
    /// Scans GC handle entries and marks objects referenced by strong handles (Normal and Pinned).
    /// Weak handles are skipped so their targets can be collected if otherwise unreachable.
    /// </summary>
    private static void ScanGCHandles()
    {
        if (s_handlerStore == null)
        {
            return;
        }

        Serial.WriteString("Start: ");
        Serial.WriteHex((ulong)s_gcHeapMin);
        Serial.WriteString("\nEnd: ");
        Serial.WriteHex((ulong)s_gcHeapMax);
        Serial.WriteString("\n");

        int size = (int)(s_handlerStore->End - s_handlerStore->Bump) / sizeof(GCHandle);

        var handles = new Span<GCHandle>((void*)Align((uint)s_handlerStore->Bump), size);
        for (int i = 0; i < handles.Length; i++)
        {
            if ((IntPtr)handles[i].obj != IntPtr.Zero)
            {
                // Only mark objects for Normal and Pinned handles
                // Weak handles should not keep objects alive
                if (handles[i].type >= GCHandleType.Normal)
                {
                    TryMarkRoot((nint)handles[i].obj);
                }
            }
        }
    }

    /// <summary>
    /// Scans stack roots. When the scheduler is active, scans all thread stacks and saved registers;
    /// otherwise scans the current stack from RSP to the stack end.
    /// </summary>
    private static void ScanStackRoots()
    {
        if (CosmosFeatures.SchedulerEnabled && SchedulerManager.IsEnabled)
        {
            var cpuStates = SchedulerManager.GetAllCpuStates();
            if (cpuStates != null)
            {
                for (int i = 0; i < cpuStates.Length; i++)
                {
                    var state = cpuStates[i];
                    if (state?.CurrentThread != null)
                    {
                        ScanThreadStack(state.CurrentThread);
                    }
                }
            }
        }
        else
        {
            nuint rsp = ContextSwitch.GetRsp();
            nuint stackEnd = rsp + Scheduler.Thread.DefaultStackSize;
            ScanMemoryRange((nint*)rsp, (nint*)stackEnd);
        }
    }

    /// <summary>
    /// Scans a thread's saved register state and stack for potential object references.
    /// </summary>
    /// <param name="thread">The thread whose stack and registers to scan.</param>
    private static void ScanThreadStack(Scheduler.Thread thread)
    {
        if (thread == null)
        {
            return;
        }

        if (thread.State != Scheduler.ThreadState.Running)
        {
            Scheduler.ThreadContext* ctx = thread.GetContext();
            if (ctx != null)
            {
#if ARCH_ARM64
                // Scan all general-purpose registers X0-X30
                TryMarkRoot((nint)ctx->X0);
                TryMarkRoot((nint)ctx->X1);
                TryMarkRoot((nint)ctx->X2);
                TryMarkRoot((nint)ctx->X3);
                TryMarkRoot((nint)ctx->X4);
                TryMarkRoot((nint)ctx->X5);
                TryMarkRoot((nint)ctx->X6);
                TryMarkRoot((nint)ctx->X7);
                TryMarkRoot((nint)ctx->X8);
                TryMarkRoot((nint)ctx->X9);
                TryMarkRoot((nint)ctx->X10);
                TryMarkRoot((nint)ctx->X11);
                TryMarkRoot((nint)ctx->X12);
                TryMarkRoot((nint)ctx->X13);
                TryMarkRoot((nint)ctx->X14);
                TryMarkRoot((nint)ctx->X15);
                TryMarkRoot((nint)ctx->X16);
                TryMarkRoot((nint)ctx->X17);
                TryMarkRoot((nint)ctx->X18);
                TryMarkRoot((nint)ctx->X19);
                TryMarkRoot((nint)ctx->X20);
                TryMarkRoot((nint)ctx->X21);
                TryMarkRoot((nint)ctx->X22);
                TryMarkRoot((nint)ctx->X23);
                TryMarkRoot((nint)ctx->X24);
                TryMarkRoot((nint)ctx->X25);
                TryMarkRoot((nint)ctx->X26);
                TryMarkRoot((nint)ctx->X27);
                TryMarkRoot((nint)ctx->X28);
                TryMarkRoot((nint)ctx->X29);  // FP (Frame Pointer)
                TryMarkRoot((nint)ctx->X30);  // LR (Link Register)
                TryMarkRoot((nint)ctx->Sp);   // Stack Pointer
                TryMarkRoot((nint)ctx->Elr);  // Exception Link Register (return address)
#else
                // x64: Scan all general-purpose registers
                TryMarkRoot((nint)ctx->Rax);
                TryMarkRoot((nint)ctx->Rbx);
                TryMarkRoot((nint)ctx->Rcx);
                TryMarkRoot((nint)ctx->Rdx);
                TryMarkRoot((nint)ctx->Rsi);
                TryMarkRoot((nint)ctx->Rdi);
                TryMarkRoot((nint)ctx->Rbp);
                TryMarkRoot((nint)ctx->R8);
                TryMarkRoot((nint)ctx->R9);
                TryMarkRoot((nint)ctx->R10);
                TryMarkRoot((nint)ctx->R11);
                TryMarkRoot((nint)ctx->R12);
                TryMarkRoot((nint)ctx->R13);
                TryMarkRoot((nint)ctx->R14);
                TryMarkRoot((nint)ctx->R15);
#endif
            }
        }

        if (thread.StackBase != 0 && thread.StackSize != 0)
        {
            nuint stackStart = thread.StackPointer;
            nuint stackEnd = thread.StackBase + thread.StackSize;
            if (stackStart < stackEnd)
            {
                ScanMemoryRange((nint*)stackStart, (nint*)stackEnd);
            }
        }
    }

    /// <summary>
    /// Scans static GC roots from all loaded managed modules.
    /// </summary>
    private static void ScanStaticRoots()
    {
        var modules = ManagedModule.Modules;
        if (modules == null)
        {
            return;
        }

        int moduleCount = ManagedModule.ModuleCount;
        for (int i = 0; i < moduleCount; i++)
        {
            TypeManager* tm = modules[i].AsTypeManager();
            if (tm == null)
            {
                continue;
            }

            nint gcStaticSection = tm->GetModuleSection(ReadyToRunSectionType.GCStaticRegion, out int length);
            if (gcStaticSection != 0 && length > 0)
            {
                ScanGCStaticRegion((byte*)gcStaticSection, length);
            }
        }
    }

    /// <summary>
    /// Scans a GCStaticRegion section for object references in static fields.
    /// </summary>
    /// <param name="regionStart">Start of the GCStaticRegion data.</param>
    /// <param name="length">Length of the region in bytes.</param>
    private static void ScanGCStaticRegion(byte* regionStart, int length)
    {
        byte* regionEnd = regionStart + length;

        for (byte* block = regionStart;
             block < regionEnd;
             block += MethodTable.SupportsRelativePointers ? sizeof(int) : sizeof(nint))
        {
            nint* pBlock;
            if (MethodTable.SupportsRelativePointers)
            {
                pBlock = (nint*)((byte*)block + *(int*)block);
            }
            else
            {
                pBlock = *(nint**)block;
            }

            if (pBlock == null)
            {
                continue;
            }

            nint value = *pBlock;
            if ((value & GCStaticRegionConstants.Mask) != 0)
            {
                continue;
            }

            if (value != 0)
            {
                TryMarkRoot(value);
            }
        }
    }

    /// <summary>
    /// Scans a contiguous memory range for potential object references (conservative scanning).
    /// </summary>
    /// <param name="start">Pointer to the first word to scan.</param>
    /// <param name="end">Pointer past the last word to scan.</param>
    private static void ScanMemoryRange(nint* start, nint* end)
    {
        for (nint* ptr = start; ptr < end; ptr++)
        {
            TryMarkRoot(*ptr);
        }
    }

    /// <summary>
    /// Attempts to mark a potential object reference. Validates that the pointer looks like a
    /// valid GC object (MethodTable outside heap) before marking and enumerating its references.
    /// Uses an iterative mark stack to avoid deep recursion.
    /// </summary>
    /// <param name="value">Potential object pointer to investigate.</param>
    [MethodImpl(MethodImplOptions.NoOptimization)]
    private static void TryMarkRoot(nint value)
    {
        PushMarkStack(value);

        while (s_markStackCount > 0)
        {
            nint ptr = PopMarkStack();
            var obj = (GCObject*)ptr;

            // Validate MethodTable - must point outside heap (to kernel code)
            nuint mtPtr = (nuint)obj->MethodTable & ~(nuint)1;
            if (mtPtr == 0 || IsInGCHeap((nint)mtPtr))
            {
                continue;
            }

            if (obj->IsMarked)
            {
                continue;
            }

            obj->Mark();

            MethodTable* mt = obj->GetMethodTable();
            if (mt->ContainsGCPointers)
            {
                EnumerateReferences(obj, mt);
            }
        }
    }

    /// <summary>
    /// Enumerates object references described by the GCDesc and pushes them onto the mark stack.
    /// Handles both fixed-layout objects (positive series count) and arrays of structs (negative series count).
    /// </summary>
    /// <param name="obj">The object whose references to enumerate.</param>
    /// <param name="mt">The object's MethodTable (must have <c>ContainsGCPointers</c> set).</param>
    private static void EnumerateReferences(GCObject* obj, MethodTable* mt)
    {
        nint numSeries = ((nint*)mt)[-1];
        if (numSeries == 0)
        {
            return;
        }

        var cur = (GCDescSeries*)((nint*)mt - 1) - 1;

        if (numSeries > 0)
        {
            uint objectSize = obj->ComputeSize();
            GCDescSeries* last = cur - numSeries + 1;

            do
            {
                nint size = cur->SeriesSize + (nint)objectSize;
                nint offset = cur->StartOffset;
                var ptr = (nint*)((nint)obj + offset);

                for (nint i = 0; i < size / IntPtr.Size; i++)
                {
                    nint refValue = ptr[i];
                    if (refValue != 0 && IsInGCHeap(refValue))
                    {
                        PushMarkStack(refValue);
                    }
                }

                cur--;
            } while (cur >= last);
        }
        else
        {
            nint offset = ((nint*)mt)[-2];
            var valSeries = (ValSerieItem*)((nint*)mt - 2) - 1;

            // Start at the offset
            var ptr = (nint*)((nint)obj + offset);

            // Retrieve the length of the array
            int length = obj->Length;

            // Repeat the loop for each element in the array
            for (int item = 0; item < length; item++)
            {
                for (int i = 0; i > numSeries; i--)
                {
                    // i is negative, so this is going backwards
                    ValSerieItem* valSerieItem = valSeries + i;

                    // Read valSerieItem->Nptrs pointers
                    for (int j = 0; j < valSerieItem->Nptrs; j++)
                    {
                        nint refValue = (nint)ptr;
                        if (refValue != 0 && IsInGCHeap(refValue))
                        {
                            PushMarkStack(refValue);
                        }

                        ptr++;
                    }

                    // Skip valSerieItem->Skip bytes
                    ptr = (nint*)((nint)ptr + valSerieItem->Skip);
                }
            }
        }
    }

    #endregion

    #region Sweep Phase

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

    #endregion

    #region Helpers

    /// <summary>
    /// Pushes a potential object pointer onto the mark stack. Expands the stack if full.
    /// </summary>
    /// <param name="ptr">The pointer to push.</param>
    private static void PushMarkStack(nint ptr)
    {
        if (s_markStackCount >= s_markStackCapacity)
        {
            // Expand mark stack
            ulong newPageCount = (s_markStackPageCount + 1) * 2;
            nint* newStack = (nint*)PageAllocator.AllocPages(PageType.Unmanaged, newPageCount, true);
            if (newStack == null)
            {
                Serial.WriteString("[GC] WARNING: Mark stack overflow\n");
                return;
            }

            for (int i = 0; i < s_markStackCount; i++)
            {
                newStack[i] = s_markStack[i];
            }

            PageAllocator.Free(s_markStack);
            s_markStack = newStack;
            s_markStackCapacity = (int)(newPageCount * PageAllocator.PageSize / (ulong)sizeof(nint));
            s_markStackPageCount = newPageCount;
        }

        s_markStack[s_markStackCount++] = ptr;
    }

    /// <summary>
    /// Pops the top entry from the mark stack.
    /// </summary>
    /// <returns>The popped pointer value, or <c>0</c> if the stack is empty.</returns>
    private static nint PopMarkStack()
    {
        return s_markStackCount > 0 ? s_markStack[--s_markStackCount] : 0;
    }

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

    #endregion
}
