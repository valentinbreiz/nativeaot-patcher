// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory.Heap;
using Cosmos.Kernel.Core.Runtime;
using Cosmos.Kernel.Core.Scheduler;
using Internal.Runtime;

namespace Cosmos.Kernel.Core.Memory;

#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct GCObject
{
    public unsafe MethodTable* MethodTable;
    public int Length; 

    public unsafe uint ComputeSize()
    {
        if (MethodTable->HasComponentSize)
        {
            return MethodTable->BaseSize + (uint)Length * MethodTable->ComponentSize;
        }
        else
        {
            // Regular object
            return MethodTable->RawBaseSize;
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct MemorySegment
{
    public unsafe MemorySegment* Next;
    public IntPtr Start;
    public IntPtr Current;
    public IntPtr End;
}

/// <summary>
/// GCDesc series descriptor for regular objects and reference arrays.
/// Stored immediately before the MethodTable pointer.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct GCDescSeries
{
    /// <summary>
    /// Size of the series. For regular objects, this is negative (-BaseSize + actual_series_size).
    /// To get actual size: seriessize + objectSize.
    /// </summary>
    public nint SeriesSize;

    /// <summary>
    /// Offset from object start where the series begins.
    /// </summary>
    public nint StartOffset;
}

/// <summary>
/// GCDesc series item for value type arrays with embedded references.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct ValSeriesItem
{
    /// <summary>
    /// Number of pointers in this series.
    /// </summary>
    public uint NumPtrs;

    /// <summary>
    /// Bytes to skip after these pointers.
    /// </summary>
    public uint Skip;
}

/// <summary>
/// Utilities for reading GCDesc information from MethodTables.
/// GCDesc is stored immediately BEFORE the MethodTable pointer in memory.
/// </summary>
public static unsafe class GCDesc
{
    /// <summary>
    /// Gets the number of GCDesc series for a MethodTable.
    /// Positive = regular object or reference array.
    /// Negative = value type array with embedded references.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static nint GetNumSeries(MethodTable* mt)
    {
        // NumSeries is stored at (MethodTable* - sizeof(nint))
        return *((nint*)mt - 1);
    }

    /// <summary>
    /// Gets the highest (first) GCDesc series pointer.
    /// Series are stored in decreasing address order before NumSeries.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static GCDescSeries* GetHighestSeries(MethodTable* mt)
    {
        // First series is at (MethodTable* - sizeof(nint) - sizeof(GCDescSeries))
        return (GCDescSeries*)((nint*)mt - 1) - 1;
    }

    /// <summary>
    /// Gets the lowest (last) series given the highest and count.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static GCDescSeries* GetLowestSeries(GCDescSeries* highest, nint numSeries)
    {
        return highest - numSeries + 1;
    }
}

/// <summary>
/// Mark-and-Sweep Garbage Collector implementation based on Kevin Gosse's approach.
/// Uses GCDesc for precise reference enumeration.
/// </summary>
public static unsafe class GarbageCollector
{
    /// <summary>
    /// Preferred size for GC memory segments. There may be segments with different sizes.
    /// </summary>
    private static uint MAX_SEGMENT_SIZE = (uint)((uint)PageAllocator.PageSize);
    public static bool IsEnabled => true;
    private const nint MethodTableMarkBit = 1;
    private static bool _initialized;
    private static nint* _markStack;
    private static int _markStackCapacity = (int)(PageAllocator.PageSize / (ulong)sizeof(nint));
    private static int _markStackCount;
    private static ulong _markStackPageCount;
    private static MemorySegment* _firstSegment;
    private static MemorySegment* _lastSegment;
    private static MethodTable*  _freeObjMethodTable;
    private static ulong _segment_count;

    // Statistics
    private static int _totalCollections;
    private static int _totalObjectsFreed;

    /// <summary>
    /// Initializes the garbage collector. Called automatically on first collection.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized)
            return;

        Serial.WriteString("[GC] Initializing garbage collector\n");

        // Allocate mark stack on unmanaged pages so it never gets swept
        ulong bytes = (ulong)_markStackCapacity * (ulong)sizeof(nint);
        _markStackPageCount = (bytes + PageAllocator.PageSize - 1) / PageAllocator.PageSize;
        _markStack = (nint*)PageAllocator.AllocPages(PageType.Unmanaged, _markStackPageCount, true);
        if (_markStack == null)
        {
            Serial.WriteString("[GC] ERROR: Failed to allocate mark stack\n");
            return;
        }
        
        // Allocate first memory segment
        var segment = AllocSegment((uint)(MAX_SEGMENT_SIZE - sizeof(MemorySegment)));
        _firstSegment = segment;
        _lastSegment = segment;
        _freeObjMethodTable = MethodTable.Of<Free>();

        _markStackCount = 0;
        _initialized = true;

        Serial.WriteString("[GC] Garbage collector initialized\n");
    }
    
    /// <summary>
    /// Allocates a new memory segment for the GC heap.
    /// </summary>
    /// <param name="size">The size of the memory segment</param>
    /// <returns>A pointer to the allocated memory segment</returns>
    private static MemorySegment* AllocSegment(uint size)
    {
        MemorySegment* segment;
        uint segmentSize = (uint)(size + sizeof(MemorySegment));

        if(segmentSize <= MAX_SEGMENT_SIZE)
        {
            segmentSize = MAX_SEGMENT_SIZE;
            segment = (MemorySegment*)PageAllocator.AllocPages(PageType.GCHeap, 1, true);
        }
        else
        {
            var pageCount = segmentSize / PageAllocator.PageSize + 1;
            segment = (MemorySegment*)PageAllocator.AllocPages(PageType.GCHeap, pageCount, true);
        
            segmentSize = (uint)(pageCount * PageAllocator.PageSize);
        }

        segment->Start = (IntPtr)Align((nint)segment + sizeof(MemorySegment));
        segment->Current = segment->Start;
        segment->End = AlignDown((nint)segment + (nint)segmentSize);

        // Make Segment walkable from the start.
        AllocFreeObject(segment);

        _segment_count++;
        return segment;
    }
    
    public static unsafe void DumbHeap()
    {
        using(InternalCpu.DisableInterruptsScope())
        {
            Serial.WriteString("[GC] Dumping heap contents:\n");
            PageAllocator.DumpPageCounts();
            Serial.WriteString("[GC] GC Segments:\n");
            Serial.WriteString("[GC] Segment Count");
            Serial.WriteNumber(_segment_count);
            Serial.WriteString("\n");
            var segment = _firstSegment;
            while(segment != null)
            {
                Serial.WriteString("[GC] Segment at: ");
                Serial.WriteHex((nuint)segment);
                Serial.WriteString(" Start: ");
                Serial.WriteHex((nuint)segment->Start);
                Serial.WriteString(" End: ");
                Serial.WriteHex((nuint)segment->End);
                Serial.WriteString("\n");
                
                var ptr = segment->Start;

                while(ptr < segment->End)
                {
                    var obj = (GCObject*)ptr;
                    
                    if(obj->MethodTable == null)
                    {
                        break;
                    }
                    
                    if(obj->MethodTable == _freeObjMethodTable)
                    {   
                        Serial.WriteString(" Object at: ");
                        Serial.WriteHex((nuint)obj);
                        Serial.WriteString(" FreeRegion, Size: ");
                        Serial.WriteNumber(obj->Length);
                        Serial.WriteString("\n");
                        ptr = Align(ptr + (nint)obj->Length);
                        continue;
                    }

                    uint objSize = obj->ComputeSize();

                    Serial.WriteString(" Object at: ");
                    Serial.WriteHex((nuint)obj);
                    Serial.WriteString(" MT: ");
                    Serial.WriteHex((nuint)obj->MethodTable);
                    Serial.WriteString(" Size: ");
                    Serial.WriteNumber(objSize);

                    Serial.WriteString("\n");

                    if (obj->MethodTable->ContainsGCPointers)
                    {
                        EnumerateObjectReferences(obj, &PrintRef);
                    }

                    ptr = Align(ptr + (nint)objSize);
                }
                segment = segment->Next;
            }
            Serial.WriteString("[GC] Dumb heap allocation complete\n");
            while (true) ;
        }
    }

    private static void PrintRef(nint refPtr)
    {
        Serial.WriteString("  Ref: ");
        Serial.WriteHex((uint)refPtr);
        var obj = (GCObject*)refPtr;
        if(obj->MethodTable == null)
        {
            Serial.WriteString(" Null Ref\n");
            return;
        }
        Serial.WriteString(" MT: ");
        Serial.WriteHex((uint)obj->MethodTable);
        Serial.WriteString(" Size: ");
        Serial.WriteNumber(obj->ComputeSize());
        Serial.WriteString("\n");
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nint Align(nint address) => (address + (IntPtr.Size - 1)) & ~(IntPtr.Size - 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nint AlignDown(nint address) => address & ~(IntPtr.Size - 1);
    
    internal static GCObject* AllocObject(nint size, uint flags)
    {
        var segment = _lastSegment;
        var Allocsize = Align(size);
    
        if (segment->Current + Allocsize > segment->End)
        {
            using (InternalCpu.DisableInterruptsScope())
            {
                var segmentSize = (uint)global::System.Math.Max(Allocsize, MAX_SEGMENT_SIZE - sizeof(MemorySegment));

                Serial.WriteString("[GC] New Segment needed for allocation of size ");
                Serial.WriteNumber(segmentSize);
                Serial.WriteString("\n");

                var newSegment = AllocSegment(segmentSize);
                _lastSegment->Next = newSegment;
                _lastSegment = newSegment;

                segment = newSegment;
            }
        }

        GCObject* result = (GCObject*)Align(segment->Current);
        segment->Current = Align(segment->Current + Allocsize);
        //MemoryOp.MemSet((byte*)result, 0, sizeof(GCObject));
        result->Length = 0;

        //if(segment->Current + (IntPtr.Size * 3) > segment->End) AllocFreeObject(segment);

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AllocFreeObject(MemorySegment* segment)
    {
        GCObject* freeObj = (GCObject*)AlignDown(segment->Current);      
        freeObj->MethodTable = _freeObjMethodTable;
        freeObj->Length = (int)(segment->End - segment->Current);
    }
    internal static nint RegisterFrozenSegment(nint pSegmentStart, nuint allocSize, nuint commitSize, nuint reservedSize)
    {
        return pSegmentStart;
    }
    internal static void UpdateFrozenSegment(nint seg, nint allocated, nint committed)
    {

    }

    /// <summary>
    /// Performs a full garbage collection cycle.
    /// </summary>
    /// <returns>Number of objects freed.</returns>
    public static int Collect()
    {
        if (!_initialized)
            return 0; // Failed to initialize

        int freedCount;
        using (InternalCpu.DisableInterruptsScope())
        {
            Serial.WriteString("[GC] Starting collection cycle #");
            Serial.WriteNumber((uint)_totalCollections + 1);
            Serial.WriteString("\n");

            // Phase 1: Mark all reachable objects
            MarkPhase();

            // Phase 2: Sweep and free unreachable objects
            freedCount = SweepPhase();

            _totalCollections++;
            _totalObjectsFreed += freedCount;

            Serial.WriteString("[GC] Collection complete. Freed ");
            Serial.WriteNumber((uint)freedCount);
            Serial.WriteString(" objects\n");
        }

        DumbHeap();

        return freedCount;
    }

    /// <summary>
    /// Gets collection statistics.
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


        // Scan all roots
        ScanStackRoots();
        ScanStaticRoots();
        ProcessMarkStack();
        ScanMemorySegments();

        // Process mark stack (DFS traversal)
        //ProcessMarkStack();
    }
    private static void ScanMemorySegments()
    {
        var segment = _firstSegment;

        while(segment->Next != null)
        {
            var ptr = segment->Start;
            while(ptr < segment->End)
            {
                var obj = (GCObject*)ptr;
                
                if(obj->MethodTable == null)
                {
                    break;
                }
                
                if(obj->MethodTable == _freeObjMethodTable)
                {
                    ptr = Align(ptr + obj->Length);
                    continue;
                }

                uint objSize = obj->ComputeSize();

                if(objSize == 0)
                {
                    break;
                }

                if (obj->MethodTable->ContainsGCPointers)
                {
                    EnumerateObjectReferences(obj, &MarkObject);
                }

                ptr = Align(ptr + (nint)objSize);
            }
            segment = segment->Next;
        }
    }

    private static void ProcessMarkStack()
    {
        while (_markStackCount > 0)
        {
            nint ptr = PopMarkStack();
            if (ptr == 0) continue;

            if (!IsManagedObject((byte*)ptr))
                continue;

            if (IsMarked((void*)ptr))
                continue; // Already marked

            // Mark the object
            MarkObject(ptr);

            // Enumerate and push references
            EnumerateObjectReferences((void*)ptr, &PushIfValidHeapPointer);
        }
    }

    private static void PushIfValidHeapPointer(nint ptr)
    {
        if (ptr != 0 && IsValidHeapPointer(ptr))
        {
            PushMarkStack(ptr);
        }
    }

    private static void ScanStackRoots()
    {
        if (CosmosFeatures.SchedulerEnabled && SchedulerManager.IsEnabled)
        {
            // Multi-threaded: scan all CPU states
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
            // Single-threaded: scan current stack conservatively
            nuint rsp = ContextSwitch.GetRsp();
            // Use default thread stack size
            nuint stackEnd = rsp + Scheduler.Thread.DefaultStackSize;
            ScanMemoryRange((nint*)rsp, (nint*)stackEnd);
        }
    }

    private static void ScanThreadStack(Scheduler.Thread thread)
    {
        if (thread == null) return;

        // Scan saved registers from context
        if (thread.State != Scheduler.ThreadState.Running)
        {
            Scheduler.ThreadContext* ctx = thread.GetContext();
            if (ctx != null)
            {
#if ARCH_X64
                TryAddRoot((nint)ctx->Rax);
                TryAddRoot((nint)ctx->Rbx);
                TryAddRoot((nint)ctx->Rcx);
                TryAddRoot((nint)ctx->Rdx);
                TryAddRoot((nint)ctx->Rsi);
                TryAddRoot((nint)ctx->Rdi);
                TryAddRoot((nint)ctx->Rbp);
                TryAddRoot((nint)ctx->R8);
                TryAddRoot((nint)ctx->R9);
                TryAddRoot((nint)ctx->R10);
                TryAddRoot((nint)ctx->R11);
                TryAddRoot((nint)ctx->R12);
                TryAddRoot((nint)ctx->R13);
                TryAddRoot((nint)ctx->R14);
                TryAddRoot((nint)ctx->R15);
#endif
            }
        }

        // Scan stack memory
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

    private static void ScanStaticRoots()
    {
        var modules = ManagedModule.Modules;
        if (modules == null) return;

        for (int i = 0; i < modules.Length; i++)
        {
            TypeManager* tm = modules[i].AsTypeManager();
            if (tm == null) continue;

            nint gcStaticSection = tm->GetModuleSection(ReadyToRunSectionType.GCStaticRegion, out int length);
            if (gcStaticSection != 0 && length > 0)
            {
                ScanGCStaticRegion((byte*)gcStaticSection, length);
            }
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
            {
                pBlock = (nint*)((byte*)block + *(int*)block);
            }
            else
            {
                pBlock = *(nint**)block;
            }

            if (pBlock == null) continue;

            // The value at pBlock is either a MethodTable* (uninitialized) or an object reference (initialized)
            nint value = *pBlock;

            // Skip uninitialized entries (they have the Uninitialized flag set)
            if ((value & GCStaticRegionConstants.Mask) != 0)
                continue;

            // This is an initialized static - the value is an object reference
            if (value != 0)
            {
                TryAddRoot(value);
            }
        }
    }

    private static void ScanMemoryRange(nint* start, nint* end)
    {
        // Conservative stack scanning: treat every pointer-sized value as potential reference
        for (nint* ptr = start; ptr < end; ptr++)
        {
            nint value = *ptr;
            TryAddRoot(value);
        }
    }

    private static void TryAddRoot(nint value)
    {
        if (value != 0 && IsValidHeapPointer(value))
        {
            PushMarkStack(value);
        }
    }

    #endregion

    #region Reference Enumeration

    private static void EnumerateObjectReferences(void* objPtr, delegate*<nint, void> callback)
    {
        if (objPtr == null) return;

        // Get MethodTable from object
        MethodTable* mt = GetMethodTable(objPtr);
        if (mt == null) return;

        // Quick check: does this type contain GC pointers?
        if (!mt->ContainsGCPointers)
            return;

        nint numSeries = GCDesc.GetNumSeries(mt);
        if (numSeries == 0) return;

        GCDescSeries* cur = GCDesc.GetHighestSeries(mt);

        if (numSeries > 0)
        {
            // Regular object or reference array (positive numSeries)
            GCDescSeries* last = GCDesc.GetLowestSeries(cur, numSeries);
            uint objectSize = ComputeSize(objPtr, mt);

            do
            {
                // SeriesSize is encoded as (actual_size - BaseSize), so we add objectSize
                nint size = cur->SeriesSize + (nint)objectSize;
                nint offset = cur->StartOffset;

                // Each pointer in this series
                for (nint i = 0; i < size; i += sizeof(nint))
                {
                    nint* refLoc = (nint*)((byte*)objPtr + offset + i);
                    nint refValue = *refLoc;
                    if (refValue != 0)
                    {
                        callback(refValue);
                    }
                }
                cur--;
            } while (cur >= last);
        }
        else
        {
            // Value type array with embedded references (negative numSeries)
            EnumerateValueTypeArrayReferences(objPtr, mt, numSeries, callback);
        }
    }

    private static void EnumerateValueTypeArrayReferences(void* objPtr, MethodTable* mt, nint numSeries, delegate*<nint, void> callback)
    {
        // For value type arrays, the GCDesc contains ValSeriesItems
        // numSeries is negative and indicates the number of pointer series per element

        int arrayLength = *(int*)((byte*)objPtr + sizeof(nint)); // Length is after MethodTable*
        if (arrayLength <= 0) return;

        uint componentSize = mt->ComponentSize;
        // GCDesc encoding: offset precedes the ValSeriesItems when numSeries is negative
        nint offset = *((nint*)mt - 2);

        // Get the ValSeriesItems (stored after the offset)
        ValSeriesItem* valSeries = (ValSeriesItem*)((nint*)mt - 2) - 1;
        int numValSeries = (int)(-numSeries);

        for (int elemIdx = 0; elemIdx < arrayLength; elemIdx++)
        {
            byte* ptr = (byte*)objPtr + offset + (uint)elemIdx * componentSize;

            for (int seriesIdx = 0; seriesIdx < numValSeries; seriesIdx++)
            {
                ValSeriesItem* item = valSeries - seriesIdx;
                uint numPtrs = item->NumPtrs;
                uint skip = item->Skip;

                // Enumerate pointers in this series
                for (uint p = 0; p < numPtrs; p++)
                {
                    nint* refLoc = (nint*)(ptr + p * (uint)sizeof(nint));
                    nint refValue = *refLoc;
                    if (refValue != 0)
                    {
                        callback(refValue);
                    }
                }

                ptr += numPtrs * (uint)sizeof(nint) + skip;
            }
        }
    }

    private static uint ComputeSize(void* objPtr, MethodTable* mt)
    {
        if (mt->HasComponentSize)
        {
            // Array or string - size depends on element count
            int length = *(int*)((byte*)objPtr + sizeof(nint));
            return mt->BaseSize + (uint)length * mt->ComponentSize;
        }
        else
        {
            // Regular object
            return mt->RawBaseSize;
        }
    }

    #endregion

    #region Sweep Phase

    private static int SweepPhase()
    {
        int totalFreed = 0;

        totalFreed += SweepMemorySegments();
        //totalFreed += SweepSmallHeap();
        //totalFreed += SweepMediumHeap();
        //totalFreed += SweepLargeHeap();

        return totalFreed;
    }

    private static int SweepMemorySegments()
    {
        int freed = 0;    
        var segment = _firstSegment;

        while(segment->Next != null)
        {
            var ptr = segment->Start;
            while(ptr < segment->End)
            {
                var obj = (GCObject*)ptr;
                
                if(obj->MethodTable == null)
                {
                    break;
                }
                
                if(obj->MethodTable == _freeObjMethodTable)
                {
                    ptr = Align(ptr + obj->Length);
                    continue;
                }

                uint objSize = obj->ComputeSize();

                if (!IsMarked(obj))
                {
                    freed++;
                    MemoryOp.MemSet((byte*)obj, 0, (int)objSize);
                    obj->MethodTable = _freeObjMethodTable;
                    obj->Length = (int)objSize;
                }
                else
                {
                    UnmarkObject(obj);
                }


                ptr = Align(ptr + (nint)objSize);
            }
            segment = segment->Next;
        }

        return freed;
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

            // IMPORTANT: Only sweep managed objects (those with a valid MethodTable)
            // Raw allocations (like the mark stack) don't have a MethodTable and should be skipped
            if (!IsManagedObject(objPtr))
                continue;

            if (!IsMarked(objPtr))
            {
                // Unmarked - free the object
                SmallHeap.Free(objPtr);
                freed++;
            }
            else
            {
                // Marked - clear the mark for next cycle
                UnmarkObject(objPtr);
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

            // Only sweep managed objects
            if (!IsManagedObject(objPtr))
                continue;

            if (!IsMarked(objPtr))
            {
                // Unmarked - free the object
                MediumHeap.Free(objPtr);
                freed++;
            }
            else
            {
                // Marked - clear the mark
                UnmarkObject(objPtr);
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

            // Only sweep managed objects
            if (!IsManagedObject(objPtr))
                continue;

            if (!IsMarked(objPtr))
            {
                // Unmarked - free the object
                LargeHeap.Free(objPtr);
                freed++;
            }
            else
            {
                // Marked - clear the mark
                UnmarkObject(objPtr);
            }
        }

        return freed;
    }

    #endregion

    #region Helper Methods

    private static void PushMarkStack(nint ptr)
    {
        if (_markStackCount >= _markStackCapacity)
        {
            // Expand mark stack
            int newCapacity = _markStackCapacity * 2;
            ulong newBytes = (ulong)newCapacity * (ulong)sizeof(nint);
            ulong newPageCount = (newBytes + PageAllocator.PageSize - 1) / PageAllocator.PageSize;
            nint* newStack = (nint*)PageAllocator.AllocPages(PageType.Unmanaged, newPageCount, true);
            if (newStack == null)
            {
                Serial.WriteString("[GC] WARNING: Mark stack overflow, cannot expand\n");
                return;
            }

            // Copy existing entries
            for (int i = 0; i < _markStackCount; i++)
            {
                newStack[i] = _markStack[i];
            }

            // Free old stack and use new one
            PageAllocator.Free(_markStack);
            _markStack = newStack;
            _markStackCapacity = newCapacity;
            _markStackPageCount = newPageCount;
        }

        _markStack[_markStackCount++] = ptr;
    }

    private static nint PopMarkStack()
    {
        if (_markStackCount <= 0) return 0;
        return _markStack[--_markStackCount];
    }

    private static bool IsValidHeapPointer(nint ptr)
    {
        // Check if pointer is within managed heap range
        if (ptr < (nint)PageAllocator.RamStart)
            return false;

        byte* heapEnd = PageAllocator.RamStart + PageAllocator.RamSize;
        if (ptr >= (nint)heapEnd)
            return false;

        // Check if page type is a heap type
        PageType type = PageAllocator.GetPageType((void*)ptr);
        return type == PageType.HeapSmall ||
               type == PageType.HeapMedium ||
               type == PageType.HeapLarge;
    }

    /// <summary>
    /// Checks if an allocation is a managed object (has a valid MethodTable pointer).
    /// Raw allocations (like the mark stack) don't have a MethodTable and should not be swept.
    /// </summary>
    private static bool IsManagedObject(byte* objPtr)
    {
        if (objPtr == null)
            return false;

        // Read the first pointer-sized value - for managed objects this is the MethodTable*
        nint potentialMT = *(nint*)objPtr;
        nint mt = potentialMT & ~MethodTableMarkBit;

        // Null is not a valid MethodTable
        if (mt == 0)
            return false;

        // Reject low addresses when the heap lives in higher-half mapping
        bool heapIsHigherHalf = ((ulong)PageAllocator.RamStart >> 63) != 0;
        if (heapIsHigherHalf && ((ulong)mt >> 63) == 0)
            return false;

        // MethodTable pointers point to static data in the kernel image, NOT to heap memory.
        // If the "MethodTable" pointer points into the heap, this is not a managed object.
        if (mt >= (nint)PageAllocator.RamStart)
        {
            byte* heapEnd = PageAllocator.RamStart + PageAllocator.RamSize;
            if (mt < (nint)heapEnd)
                return false; // Points into heap - not a MethodTable
        }

        // Additional sanity check: MethodTable should be reasonably aligned
        if ((mt & 0x7) != 0)
            return false; // Not 8-byte aligned

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static MethodTable* GetMethodTable(void* objPtr)
    {
        nint raw = *(nint*)objPtr;
        return (MethodTable*)(raw & ~MethodTableMarkBit);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsMarked(void* objPtr)
    {
        nint raw = *(nint*)objPtr;
        return (raw & MethodTableMarkBit) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void MarkObject(nint objPtr)
    {
        nint raw = *(nint*)objPtr;
        *(nint*)objPtr = raw | MethodTableMarkBit;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void UnmarkObject(void* objPtr)
    {
        nint raw = *(nint*)objPtr;
        *(nint*)objPtr = raw & ~MethodTableMarkBit;
    }

    #endregion

    /// <summary>
    /// A marker used inside the memory segments to keep them walkable
    /// </summary>
    private struct Free
    {
    }
}
