// This code is licensed under MIT license (see LICENSE for details)
// Clean GC implementation with free list allocation

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory.Heap;
using Cosmos.Kernel.Core.Runtime;
using Cosmos.Kernel.Core.Scheduler;
using Internal.Runtime;

namespace Cosmos.Kernel.Core.Memory;

#pragma warning disable CS8500

/// <summary>
/// Free block in the GC heap. Used for free list management.
/// Laid out to be walkable like a GCObject.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct FreeBlock
{
    public MethodTable* MethodTable;  // Points to _freeMethodTable marker
    public int Size;                   // Size of this free block (matches GCObject.Length position)
    public FreeBlock* Next;            // Next free block in this size class
}

/// <summary>
/// Marker type for free blocks. Used to get a valid MethodTable pointer.
/// </summary>
internal struct FreeMarker { }

/// <summary>
/// GC heap segment for contiguous allocations.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct GCSegment
{
    public GCSegment* Next;
    public byte* Start;
    public byte* End;
    public byte* Bump;        // Current bump allocation pointer
    public uint TotalSize;
    public uint UsedSize;
}

/// <summary>
/// Mark-and-Sweep Garbage Collector with free list allocation.
/// </summary>
public static unsafe partial class GarbageCollector
{
    // Free list size classes: 16, 32, 64, ... 32768 (powers of two)
    private const int NumSizeClasses = 12;
    private const uint MinSizeClass = 16;

    // Free lists indexed by size class
    private static FreeBlock** _freeLists;
    private static bool _freeListsInitialized;

    // Free block marker MethodTable
    private static MethodTable* _freeMethodTable;

    // GC Segments
    private static GCSegment* _segments;
    private static GCSegment* _currentSegment;
    private static GCSegment* _lastSegment;
    private static GCSegment* _tailSegment;
    private static uint MAX_SEGMENT_SIZE = (uint)((uint)PageAllocator.PageSize);

    // Heap range cache (fast pre-check for IsInGCHeap)
    private static byte* _gcHeapMin;
    private static byte* _gcHeapMax;
    private static bool _heapRangeDirty;

    // Mark stack
    private static nint* _markStack;
    private static int _markStackCapacity;
    private static int _markStackCount;

    // State
    private static bool _initialized;
    private static int _totalCollections;
    private static int _totalObjectsFreed;

    /// <summary>
    /// Returns true if the GC is enabled.
    /// </summary>
    public static bool IsEnabled => true;

    // Minimum block size (must hold FreeBlock header) - 24 bytes on x64
    private const uint MinBlockSize = 24;

    /// <summary>
    /// Initializes the garbage collector.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;

        Serial.WriteString("[GC] Initializing with free list allocator\n");

        // Allocate free list array using page allocator (not GC heap)
        ulong freeListBytes = (ulong)(NumSizeClasses * sizeof(FreeBlock*));
        _freeLists = (FreeBlock**)PageAllocator.AllocPages(PageType.Unmanaged, 1, true);
        if (_freeLists == null)
        {
            Serial.WriteString("[GC] ERROR: Failed to allocate free lists\n");
            return;
        }
        for (int i = 0; i < NumSizeClasses; i++)
            _freeLists[i] = null;
        _freeListsInitialized = true;

        // Get the free marker MethodTable
        _freeMethodTable = MethodTable.Of<FreeMarker>();

        // Allocate initial segment
        _currentSegment = AllocateSegment(MAX_SEGMENT_SIZE);
        _segments = _currentSegment;
        _lastSegment = _currentSegment;
        _tailSegment = _currentSegment;
        _heapRangeDirty = true;
        RecomputeHeapRange();
        if (_segments == null)
        {
            Serial.WriteString("[GC] ERROR: Failed to allocate initial segment\n");
            return;
        }

        // Allocate mark stack
        _markStackCapacity = 4096;
        _markStack = (nint*)PageAllocator.AllocPages(PageType.Unmanaged, 1, true);
        if (_markStack == null)
        {
            Serial.WriteString("[GC] ERROR: Failed to allocate mark stack\n");
            return;
        }
        _markStackCount = 0;

        InitializeGCHandleStore();

        _initialized = true;
        Serial.WriteString("[GC] Initialization complete\n");
    }

    /// <summary>
    /// Allocates a new GC segment.
    /// </summary>
    private static GCSegment* AllocateSegment(uint requestedSize)
    {
        uint size = requestedSize < MAX_SEGMENT_SIZE ? MAX_SEGMENT_SIZE : requestedSize;
        uint totalSize = size + (uint)sizeof(GCSegment);
        ulong pageCount = (totalSize + PageAllocator.PageSize - 1) / PageAllocator.PageSize;

        byte* memory = (byte*)PageAllocator.AllocPages(PageType.GCHeap, pageCount, true);
        if (memory == null) return null;

        GCSegment* segment = (GCSegment*)memory;
        segment->Next = null;
        segment->Start = memory + Align((uint)sizeof(GCSegment));
        segment->End = memory + (pageCount * PageAllocator.PageSize);
        segment->Bump = segment->Start;
        segment->TotalSize = (uint)(segment->End - segment->Start);
        segment->UsedSize = 0;

        return segment;
    }

    /// <summary>
    /// Aligns size to pointer boundary.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Align(uint size)
    {
        return (size + ((uint)sizeof(nint) - 1)) & ~((uint)sizeof(nint) - 1);
    }

    /// <summary>
    /// Allocates memory for a GC object. Called by runtime.
    /// </summary>
    internal static GCObject* AllocObject(nint size, GC_ALLOC_FLAGS flags)
    {
        if (!_initialized) Initialize();

        // Check for pinned object allocation
        if ((flags & GC_ALLOC_FLAGS.GC_ALLOC_PINNED_OBJECT_HEAP) != 0)
        {
            return AllocPinnedObject(size, flags);
        }

        uint allocSize = Align((uint)size);
        if (allocSize < MinBlockSize)
            allocSize = MinBlockSize;

        // Try free list allocation first
        void* result = AllocFromFreeList(allocSize);
        if (result != null)
            return (GCObject*)result;

        // Try fast bump allocation from last segment
        result = BumpAllocInSegment(_lastSegment, allocSize);
        if (result != null)
            return (GCObject*)result;

        // Slow path: walk segments from _lastSegment and append if needed
        result = AllocateObjectSlow(allocSize);
        if (result != null)
            return (GCObject*)result;

        // Last resort: collect and retry
        Collect();

        result = AllocFromFreeList(allocSize);
        if (result != null)
            return (GCObject*)result;

        result = AllocateObjectSlow(allocSize);
        return (GCObject*)result;
    }

    /// <summary>
    /// Tries to allocate from free lists.
    /// </summary>
    private static void* AllocFromFreeList(uint size)
    {
        if (!_freeListsInitialized) return null;

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
        if (sizeClass < 0) return null; // Too large

        // Try this size class and larger
        for (int i = sizeClass; i < NumSizeClasses; i++)
        {
            FreeBlock* block = _freeLists[i];
            if (block == null) continue;

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
                        prev->Next = block->Next;
                    else
                        _freeLists[i] = block->Next;

                    // Split if remainder is usable
                    if (remainder >= MinBlockSize)
                    {
                        FreeBlock* split = (FreeBlock*)((byte*)block + size);
                        split->MethodTable = _freeMethodTable;
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

    private static void* BumpAllocInSegment(GCSegment* segment, uint size)
    {
        if (segment == null)
            return null;

        byte* newBump = segment->Bump + size;
        if (newBump <= segment->End)
        {
            void* result = segment->Bump;
            segment->Bump = newBump;
            segment->UsedSize += size;
            _currentSegment = segment;
            _lastSegment = segment;
            return result;
        }

        return null;
    }

    private static void* AllocateObjectSlow(uint size)
    {
        if (_segments == null)
            return null;

        if (_lastSegment == null)
            _lastSegment = _segments;

        GCSegment* start = _lastSegment;

        // Pass 1: from _lastSegment to end
        for (GCSegment* seg = start; seg != null; seg = seg->Next)
        {
            void* result = BumpAllocInSegment(seg, size);
            if (result != null)
                return result;
        }

        // Pass 2: from head to _lastSegment (exclusive)
        for (GCSegment* seg = _segments; seg != start; seg = seg->Next)
        {
            void* result = BumpAllocInSegment(seg, size);
            if (result != null)
                return result;
        }

        // No segment fits: allocate and append a new segment at tail
        GCSegment* newSegment = AllocateSegment(size);
        if (newSegment == null)
            return null;

        AppendSegment(newSegment);
        _lastSegment = newSegment;
        _currentSegment = newSegment;

        return BumpAllocInSegment(newSegment, size);
    }

    private static void AppendSegment(GCSegment* segment)
    {
        if (segment == null) return;

        segment->Next = null;

        if (_segments == null)
        {
            _segments = segment;
            _tailSegment = segment;
            _heapRangeDirty = true;
            return;
        }

        if (_tailSegment != null)
        {
            _tailSegment->Next = segment;
            _tailSegment = segment;
            _heapRangeDirty = true;
            return;
        }

        // Fallback if tail is not tracked
        GCSegment* tail = _segments;
        while (tail->Next != null)
            tail = tail->Next;

        tail->Next = segment;
        _tailSegment = segment;
        _heapRangeDirty = true;
    }

    /// <summary>
    /// Adds a block to the appropriate free list.
    /// </summary>
    private static void AddToFreeList(FreeBlock* block)
    {
        if (!_freeListsInitialized || block == null || block->Size < MinBlockSize)
            return;

        block->MethodTable = _freeMethodTable;

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

        block->Next = _freeLists[sizeClass];
        _freeLists[sizeClass] = block;
    }

    /// <summary>
    /// Performs garbage collection.
    /// </summary>
    public static int Collect()
    {
        if (!_initialized) return 0;

        int freedCount;
        using (InternalCpu.DisableInterruptsScope())
        {
            Serial.WriteString("[GC] Collection #");
            Serial.WriteNumber((uint)_totalCollections + 1);
            Serial.WriteString("\n");

            // Clear free lists - will be rebuilt during sweep
            for (int i = 0; i < NumSizeClasses; i++)
                _freeLists[i] = null;

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

            _totalCollections++;
            _totalObjectsFreed += freedCount;

            Serial.WriteString("[GC] Freed ");
            Serial.WriteNumber((uint)freedCount);
            Serial.WriteString(" objects\n");
        }

        return freedCount;
    }

    /// <summary>
    /// Gets GC statistics.
    /// </summary>
    public static void GetStats(out int totalCollections, out int totalObjectsFreed)
    {
        totalCollections = _totalCollections;
        totalObjectsFreed = _totalObjectsFreed;
    }

    #region Mark Phase

    private static void MarkPhase()
    {
        _markStackCount = 0;
        ScanStackRoots();
        ScanGCHandles();
        //ScanStaticRoots();
    }

    private static void ScanGCHandles()
    {
        if (handlerStore == null) return;
        Serial.WriteString("Start: ");
        Serial.WriteHex((ulong)_gcHeapMin);
        Serial.WriteString("\nEnd: ");
        Serial.WriteHex((ulong)_gcHeapMax);
        Serial.WriteString("\n");

        int size = (int)(handlerStore->End - handlerStore->Bump) / sizeof(GCHandle);

        var handles = new Span<GCHandle>((void*)Align((uint)handlerStore->Bump), size);
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
                        ScanThreadStack(state.CurrentThread);
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

    private static void ScanThreadStack(Scheduler.Thread thread)
    {
        if (thread == null) return;

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
                ScanMemoryRange((nint*)stackStart, (nint*)stackEnd);
        }
    }

    private static void ScanStaticRoots()
    {
        var modules = ManagedModule.Modules;
        if (modules == null) return;

        int moduleCount = ManagedModule.ModuleCount;
        for (int i = 0; i < moduleCount; i++)
        {
            TypeManager* tm = modules[i].AsTypeManager();
            if (tm == null) continue;

            nint gcStaticSection = tm->GetModuleSection(ReadyToRunSectionType.GCStaticRegion, out int length);
            if (gcStaticSection != 0 && length > 0)
                ScanGCStaticRegion((byte*)gcStaticSection, length);
        }
    }

    private static void ScanGCStaticRegion(byte* regionStart, int length)
    {
        byte* regionEnd = regionStart + length;

        for (byte* block = regionStart;
             block < regionEnd;
             block += MethodTable.SupportsRelativePointers ? sizeof(int) : sizeof(nint))
        {
            nint* pBlock;
            if (MethodTable.SupportsRelativePointers)
                pBlock = (nint*)((byte*)block + *(int*)block);
            else
                pBlock = *(nint**)block;

            if (pBlock == null) continue;

            nint value = *pBlock;
            if ((value & GCStaticRegionConstants.Mask) != 0)
                continue;

            if (value != 0)
                TryMarkRoot(value);
        }
    }

    private static void ScanMemoryRange(nint* start, nint* end)
    {
        for (nint* ptr = start; ptr < end; ptr++)
            TryMarkRoot(*ptr);
    }


    [MethodImpl(MethodImplOptions.NoOptimization)]
    private static void TryMarkRoot(nint value, bool SkipValidation = false)
    {
        //if (!SkipValidation && (value == 0 || !IsInGCHeap(value)))
        //    return;

        PushMarkStack(value);

        while (_markStackCount > 0)
        {
            nint ptr = PopMarkStack();
            GCObject* obj = (GCObject*)ptr;

            // Validate MethodTable - must point outside heap (to kernel code)
            nuint mtPtr = (nuint)obj->MethodTable & ~(nuint)1;
            if (mtPtr == 0 || IsInGCHeap((nint)mtPtr))
                continue;

            if (obj->IsMarked)
                continue;

            obj->Mark();

            MethodTable* mt = obj->GetMethodTable();
            if (mt->ContainsGCPointers)
                EnumerateReferences(obj, mt);
        }
    }

    private static void EnumerateReferences(GCObject* obj, MethodTable* mt)
    {
        nint numSeries = ((nint*)mt)[-1];
        if (numSeries == 0) return;

        GCDescSeries* cur = (GCDescSeries*)((nint*)mt - 1) - 1;

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
                        PushMarkStack(refValue);
                }
                cur--;
            } while (cur >= last);
        }
        else
        {
            var offset =  ((nint*)mt)[-2];
            var valSeries = (ValSerieItem*)((nint*)mt - 2) - 1;

            // Start at the offset
            var ptr = (nint*)((nint)obj + offset);

            // Retrieve the length of the array
            var length = obj->Length;

            // Repeat the loop for each element in the array
            for (int item = 0; item < length; item++)
            {
                for (int i = 0; i > numSeries; i--)
                {
                    // i is negative, so this is going backwards
                    var valSerieItem = valSeries + i;

                    // Read valSerieItem->Nptrs pointers
                    for (int j = 0; j < valSerieItem->Nptrs; j++)
                    {
                        nint refValue = (nint)ptr;
                        if (refValue != 0 && IsInGCHeap(refValue))
                            PushMarkStack(refValue);
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

    private static int SweepPhase()
    {
        int totalFreed = 0;

        GCSegment* segment = _segments;
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

    private static int SweepSegment(GCSegment* segment)
    {
        int freed = 0;
        byte* ptr = segment->Start;
        byte* freeRunStart = null;
        uint freeRunSize = 0;

        while (ptr < segment->Bump)
        {
            GCObject* obj = (GCObject*)ptr;

            // Get MethodTable (mask off mark bit)
            MethodTable* mt = obj->GetMethodTable();

            if (mt == null)
            {
                // End of valid objects
                break;
            }

            // Check if this is a free block from previous GC
            if (mt == _freeMethodTable)
            {
                FreeBlock* freeBlock = (FreeBlock*)ptr;
                uint blockSize = (uint)freeBlock->Size;
                if (blockSize == 0 || blockSize > (uint)(segment->End - ptr))
                    break;

                // Accumulate into free run
                if (freeRunStart == null)
                    freeRunStart = ptr;
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
                    freeRunStart = ptr;
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
    /// Converts a free memory run into a FreeBlock and adds to free list.
    /// </summary>
    private static void FlushFreeRun(byte* start, uint size)
    {
        if (start == null || size < MinBlockSize)
            return;

        FreeBlock* freeBlock = (FreeBlock*)start;
        freeBlock->MethodTable = _freeMethodTable;
        freeBlock->Size = (int)size;
        freeBlock->Next = null;
        AddToFreeList(freeBlock);
    }

    /// <summary>
    /// Reorder segments as FULL -> SEMIFULL -> FREE, and free fully empty multi-page segments.
    /// </summary>
    private static void ReorderSegmentsAndFreeEmpty()
    {
        GCSegment* fullHead = null;
        GCSegment* fullTail = null;
        GCSegment* semiHead = null;
        GCSegment* semiTail = null;
        GCSegment* freeHead = null;
        GCSegment* freeTail = null;
        GCSegment* seg = _segments;

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
                    if (fullHead == null) fullHead = seg;
                    else fullTail->Next = seg;
                    fullTail = seg;
                }
                else if (isFree)
                {
                    if (freeHead == null) freeHead = seg;
                    else freeTail->Next = seg;
                    freeTail = seg;
                }
                else
                {
                    if (semiHead == null) semiHead = seg;
                    else semiTail->Next = seg;
                    semiTail = seg;
                }
            }

            seg = next;
        }

        GCSegment* newHead = null;
        GCSegment* tail = null;

        if (fullHead != null) { newHead = fullHead; tail = fullTail; }
        if (semiHead != null)
        {
            if (newHead == null) newHead = semiHead;
            else tail->Next = semiHead;
            tail = semiTail;
        }
        if (freeHead != null)
        {
            if (newHead == null) newHead = freeHead;
            else tail->Next = freeHead;
            tail = freeTail;
        }

        _segments = newHead;
        _tailSegment = tail;
        _lastSegment = semiHead != null ? semiHead : freeHead;
        _currentSegment = _lastSegment;

        _heapRangeDirty = true;
    }

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
            if (size == 0) continue; // Not allocated

            // Get object pointer
            byte* objPtr = slotPtr + SmallHeap.PrefixBytes;

            GCObject* obj = (GCObject*)objPtr;

            // Validate MethodTable - must point outside heap (to kernel code)
            nuint mtPtr = (nuint)obj->MethodTable & ~(nuint)1;
            if (mtPtr == 0 || !IsInGCHeap((nint)mtPtr))
                continue;

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

    private static int SweepMediumHeap()
    {
        int freed = 0;

        // Iterate through RAT looking for HeapMedium pages
        byte* ramStart = PageAllocator.RamStart;
        for (ulong pageIdx = 0; pageIdx < PageAllocator.TotalPageCount; pageIdx++)
        {
            PageType type = PageAllocator.GetPageType(ramStart + pageIdx * PageAllocator.PageSize);
            if (type != PageType.HeapMedium) continue;

            byte* pagePtr = ramStart + pageIdx * PageAllocator.PageSize;
            MediumHeapHeader* header = (MediumHeapHeader*)pagePtr;

            if (header->Size == 0) continue; // Not allocated

            byte* objPtr = pagePtr + MediumHeap.PrefixBytes;

            GCObject* obj = (GCObject*)objPtr;

            // Validate MethodTable - must point outside heap (to kernel code)
            nuint mtPtr = (nuint)obj->MethodTable & ~(nuint)1;
            if (mtPtr == 0 || !IsInGCHeap((nint)mtPtr))
                continue;

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

    private static int SweepLargeHeap()
    {
        int freed = 0;

        // Iterate through RAT looking for HeapLarge pages
        byte* ramStart = PageAllocator.RamStart;
        for (ulong pageIdx = 0; pageIdx < PageAllocator.TotalPageCount; pageIdx++)
        {
            PageType type = PageAllocator.GetPageType(ramStart + pageIdx * PageAllocator.PageSize);
            if (type != PageType.HeapLarge) continue;

            byte* pagePtr = ramStart + pageIdx * PageAllocator.PageSize;
            LargeHeapHeader* header = (LargeHeapHeader*)pagePtr;

            if (header->Size == 0) continue; // Not allocated

            byte* objPtr = pagePtr + LargeHeap.PrefixBytes;

            GCObject* obj = (GCObject*)objPtr;

            // Validate MethodTable - must point outside heap (to kernel code)
            nuint mtPtr = (nuint)obj->MethodTable & ~(nuint)1;
            if (mtPtr == 0 || !IsInGCHeap((nint)mtPtr))
                continue;

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

    private static void PushMarkStack(nint ptr)
    {
        if (_markStackCount >= _markStackCapacity)
        {
            // Expand mark stack
            ulong newPageCount = (_markStackPageCount + 1) * 2;
            nint* newStack = (nint*)PageAllocator.AllocPages(PageType.Unmanaged, newPageCount, true);
            if (newStack == null)
            {
                Serial.WriteString("[GC] WARNING: Mark stack overflow\n");
                return;
            }

            for (int i = 0; i < _markStackCount; i++)
                newStack[i] = _markStack[i];

            PageAllocator.Free(_markStack);
            _markStack = newStack;
            _markStackCapacity = (int)(newPageCount * PageAllocator.PageSize / (ulong)sizeof(nint));
            _markStackPageCount = newPageCount;
        }

        _markStack[_markStackCount++] = ptr;
    }

    private static ulong _markStackPageCount = 1;

    private static nint PopMarkStack()
    {
        return _markStackCount > 0 ? _markStack[--_markStackCount] : 0;
    }

    /// <summary>
    /// Checks if pointer is within any GC heap segment (including pinned).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsInGCHeap(nint ptr)
    {
        if (_heapRangeDirty)
            RecomputeHeapRange();

        byte* p = (byte*)ptr;
        if (p < _gcHeapMin || p >= _gcHeapMax)
        {
            // Check pinned heap
            return IsInPinnedHeap(ptr);
        }

        GCSegment* segment = _segments;
        while (segment != null)
        {
            if (p >= segment->Start && p < segment->End)
                return true;
            segment = segment->Next;
        }

        return IsInPinnedHeap(ptr);
    }

    private static void RecomputeHeapRange()
    {
        if (_segments == null)
        {
            _gcHeapMin = (byte*)0;
            _gcHeapMax = (byte*)0;
            _heapRangeDirty = false;
            return;
        }

        byte* min = _segments->Start;
        byte* max = _segments->End;

        for (GCSegment* seg = _segments->Next; seg != null; seg = seg->Next)
        {
            if (seg->Start < min) min = seg->Start;
            if (seg->End > max) max = seg->End;
        }

        _gcHeapMin = min;
        _gcHeapMax = max;
        _heapRangeDirty = false;
    }

    #endregion
}

/// <summary>
/// GCVal series descriptor.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ValSerieItem
{
    public uint Nptrs;
    public uint Skip;
}

/// <summary>
/// GCDesc series descriptor.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct GCDescSeries
{
    public nint SeriesSize;
    public nint StartOffset;


    public void Deconstruct(out nint size, out nint offset)
    {
        size = SeriesSize;
        offset = StartOffset;
    }
}

internal static class ObjectHeader
{
    private const uint BIT_SBLK_UNUSED = 0x80000000;
    private const uint BIT_SBLK_FINALIZER_RUN = 0x40000000;
    private const uint BIT_SBLK_GC_RESERVE = 0x20000000;


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe int* GetHeaderPtr(MethodTable** ppMethodTable)
    {
        // The header is 4 bytes before m_pEEType field on all architectures
        return (int*)ppMethodTable - 1;
    }
}
