using System.Drawing;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.Core.Memory.GarbageCollector;
using Internal.Runtime;

namespace Cosmos.Kernel.Core.Runtime;


public static unsafe class Memory
{

    // Copy past of GCMemoryInfoData & GCGenerationInfo
    // https://github.com/dotnet/runtime/blob/51cfb0e382532e43500216c755d8bd8e1a8f371d/src/libraries/System.Private.CoreLib/src/System/GCMemoryInfo.cs#L16C28-L16C44

    // The original struct do not set a layout, for safety i put one. Is it a bad idea ?
    [StructLayout(LayoutKind.Sequential)]
    public struct GCGenerationInfo
    {
        public long sizeBeforeBytes;
        public long fragmentationBeforeBytes;
        public long sizeAfterBytes;
        public long fragmentationAfterBytes;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GCMemoryInfoDataStruct
    {
        internal long _highMemoryLoadThresholdBytes;
        internal long _totalAvailableMemoryBytes;
        internal long _memoryLoadBytes;
        internal long _heapSizeBytes;
        internal long _fragmentedBytes;
        internal long _totalCommittedBytes;
        internal long _promotedBytes;
        internal long _pinnedObjectsCount;
        internal long _finalizationPendingCount;
        internal long _index;
        internal int _generation;
        internal int _pauseTimePercentage;
        internal byte _compacted;
        internal byte _concurrent;

        internal GCGenerationInfo _generationInfo0;
        internal GCGenerationInfo _generationInfo1;
        internal GCGenerationInfo _generationInfo2;
        internal GCGenerationInfo _generationInfo3;
        internal GCGenerationInfo _generationInfo4;

        internal TimeSpan _pauseDuration0;
        internal TimeSpan _pauseDuration1;
    }

    
    [RuntimeExport("RhGetMemoryInfo")]
    internal unsafe static void RhGetMemoryInfo(ref byte rawData, GCKind kind)
    {
        fixed (byte* pRawData = &rawData)
        {
            var data = (GCMemoryInfoDataStruct*)pRawData;
            GarbageCollector.SimpleMemoryInfo info = GarbageCollector.GetSimpleMemoryInfo();

            // the ration 0.5 is arbitrary, should be change in the future
            data->_highMemoryLoadThresholdBytes = (long)(PageAllocator.RamSize / 2);
            data->_totalAvailableMemoryBytes = (long)PageAllocator.RamSize;
            data->_memoryLoadBytes = (long)info.MemoryLoadBytes;
            data->_heapSizeBytes = (long)info.HeapSizeBytes;
            data->_fragmentedBytes = (long)info.FragmentedBytes;
            data->_totalCommittedBytes = (long)info.TotalCommittedBytes;
            data->_promotedBytes = (long)info.PromotedBytes;
            data->_pinnedObjectsCount = (long)info.PinnedObjectsCount;
            data->_index = info.CollectionIndex;
            data->_generation = info.CondemnedGeneration;

            // not tracked currently
            data->_finalizationPendingCount = 0;
            data->_pauseTimePercentage = 0;
            data->_compacted = 0;
            data->_concurrent = 0;

            // Populate generation info. GC is non-generational; report generation 0
            // Use last recorded before/after metrics from the GC (recorded at Collect())
            data->_generationInfo0.sizeBeforeBytes = (long)GarbageCollector.GetLastGenSizeBefore(0);
            data->_generationInfo0.fragmentationBeforeBytes = (long)GarbageCollector.GetLastGenFragmentationBefore(0);
            data->_generationInfo0.sizeAfterBytes = (long)GarbageCollector.GetLastGenSizeAfter(0);
            data->_generationInfo0.fragmentationAfterBytes = (long)GarbageCollector.GetLastGenFragmentationAfter(0);

            // Other generation infos remain zero
            data->_generationInfo1 = default;
            data->_generationInfo2 = default;
            data->_generationInfo3 = default;
            data->_generationInfo4 = default;

            data->_pauseDuration0 = TimeSpan.Zero;
            data->_pauseDuration1 = TimeSpan.Zero;
        }
    }

    [RuntimeExport("RhGetGcTotalMemory")]
    static long RhGetGcTotalMemory()
    {
        ulong heapBytes = GarbageCollector.GetHeapSizeBytes();
        if (heapBytes > long.MaxValue)
        {
            return long.MaxValue;
        }

        return (long)heapBytes;
    }

    [RuntimeExport("RhCollect")]
    internal static void RhCollect(int generation, InternalGCCollectionMode mode)
    {
        // Do not support generation for now nor modes
        GarbageCollector.Collect();
    }

    [RuntimeExport("RhGetGeneration")]
    internal static int RhGetGeneration(object obj)
    {
        nint addr = Unsafe.As<object, nint>(ref obj);
        return GarbageCollector.GetObjectGeneration(addr);
    }

    [RuntimeExport("RhGetGenerationSize")]
    internal static int RhGetGenerationSize(int gen)
    {
        // Our GC is currently non-generational. Delegate to the GC helper which
        // returns the total used bytes for generation 0 and 0 for others.
        uint size = GarbageCollector.GetGenerationSize(gen);
        if (size > int.MaxValue)
        {
            return int.MaxValue;
        }

        return (int)size;
    }

    [RuntimeExport("RhGetLastGCPercentTimeInGC")]
    internal static int RhGetLastGCPercentTimeInGC()
    {
        return GarbageCollector.GetLastGCPercentTimeInGC();
    }

    // In the runtime the handle is a pointer-to-a-pointer to an object which can be cast to the object in different ways.
    // Use HandleGetPrimary to avoid having to handle whether the USE_CHECKED_OBJECTREFS macros are defined or not.
    [RuntimeExport("RhHandleGet")]
    internal static object? RhHandleGet(IntPtr handle)
    {
        GCObject* primary = GarbageCollector.HandleGetPrimary(handle);
        if (primary == null)
        {
            return null;
        }

        nint asInt = (nint)primary;
        return Unsafe.As<nint, object>(ref asInt);
    }

    [RuntimeExport("RhGetGcCollectionCount")]
    internal static int RhGetGcCollectionCount(int generation, bool getSpecialGCCount)
    {
        if (generation != 0)
        {
            return 0;
        }

        return GarbageCollector.GetCollectionIndex();
    }

    [RuntimeExport("RhIsPromoted")]
    internal static bool RhIsPromoted(object obj)
    {
        // Only gen 0 exist can't be promoted
        return false;
    }

    [RuntimeExport("RhIsServerGc")]
    internal static bool RhIsServerGc()
    {
        return false;
    }

    [RuntimeExport("RhGetGCSegmentSize")]
    internal static ulong RhGetGCSegmentSize()
    {
        return GarbageCollector.GetGCSegmentSizeBytes();
    }

    [RuntimeExport("RhGetAllocatedBytesForCurrentThread")]
    internal static long RhGetAllocatedBytesForCurrentThread()
    {
        // Per-thread allocation accounting is not tracked currently.
        // Return 0 as a safe default.
        return 0;
    }


    [RuntimeExport("RhGetTotalAllocatedBytes")]
    internal static long RhGetTotalAllocatedBytes()
    {
        ulong allocated = GarbageCollector.GetTotalCommittedBytes();
        if (allocated > long.MaxValue)
        {
            return long.MaxValue;
        }

        return (long)allocated;
    }

    [RuntimeExport("RhNewArray")]
    internal static unsafe void* RhNewArray(MethodTable* pEEType, int length)
    {
        RhAllocateNewArray(pEEType, (uint)length, 0, out void* result);
        return result;
    }

    [RuntimeExport("RhAllocateNewArray")]
    internal static unsafe void RhAllocateNewArray(MethodTable* pArrayEEType, uint numElements, GC_ALLOC_FLAGS flags,
        out void* pResult)
    {
        uint size = pArrayEEType->BaseSize + numElements * pArrayEEType->ComponentSize;

        GCObject* result = AllocObject(size, flags);

        result->MethodTable = pArrayEEType;
        result->Length = (int)numElements;

        pResult = result;
    }

    [RuntimeExport("RhpNewArray")]
    internal static unsafe void* RhpNewArray(MethodTable* pMT, int length)
    {
        if (length < 0)
        {
            return null;
        }

        uint size = pMT->BaseSize + (uint)length * pMT->ComponentSize;

        GCObject* result = AllocObject(size);

        result->MethodTable = pMT;
        result->Length = length;

        return result;
    }

    [RuntimeExport("RhpNewPtrArrayFast")]
    internal static unsafe void* RhpNewPtrArrayFast(MethodTable* pMT, int length)
    {
        if (length < 0)
        {
            return null;
        }

        uint size = pMT->BaseSize + (uint)length * pMT->ComponentSize;

        GCObject* result = AllocObject(size);

        result->MethodTable = pMT;
        result->Length = length;

        return result;
    }
    [RuntimeExport("RhpNewArrayFast")]
    internal static unsafe void* RhpNewArrayFast(MethodTable* pMT, int length)
    {
        if (length < 0)
        {
            return null;
        }

        uint size = pMT->BaseSize + (uint)length * pMT->ComponentSize;

        GCObject* result = AllocObject(size);
        result->MethodTable = pMT;
        result->Length = length;

        return result;
    }

    [RuntimeExport("RhNewVariableSizeObject")]
    internal static unsafe void* RhNewVariableSizeObject(MethodTable* pEEType, int length)
    {
        return RhpNewArray(pEEType, length);
    }

    [RuntimeExport("RhAllocateNewObject")]
    internal static unsafe void RhAllocateNewObject(MethodTable* pEEType, GC_ALLOC_FLAGS flags, void* pResult)
    {
        GCObject* result = AllocObject(pEEType->RawBaseSize, flags);
        result->MethodTable = pEEType;

        *(void**)pResult = result;
        // as some point we should set flags   
    }

    [RuntimeExport("RhpGcSafeZeroMemory")]
    internal static unsafe byte* RhpGcSafeZeroMemory(byte* dmem, nuint size)
    {
        MemoryOp.MemSet(dmem, 0, (int)size);
        return dmem;
    }

    [RuntimeExport("RhpNewFast")]
    internal static unsafe void* RhpNewFast(MethodTable* pMT)
    {
        // Use RawBaseSize instead of BaseSize because BaseSize only works for canonical/array types
        // For generic type definitions, BaseSize contains parameter count, not the actual size
        GCObject* result = AllocObject(pMT->RawBaseSize);
        result->MethodTable = pMT;
        return (byte*)result;
    }

    [RuntimeExport("RhSpanHelpers_MemCopy")]
    private static unsafe void RhSpanHelpers_MemCopy(byte* dest, byte* src, UIntPtr len)
    {
        MemoryOp.MemCopy(dest, src, (int)len);
    }

    [RuntimeExport("memmove")]
    private static unsafe void memmove(byte* dest, byte* src, UIntPtr len)
    {
        MemoryOp.MemMove(dest, src, (int)len);
    }

    [RuntimeExport("memset")]
    private static unsafe void memset(byte* dest, int value, UIntPtr len)
    {
        MemoryOp.MemSet(dest, (byte)value, (int)len);
    }

    [RuntimeExport("RhNewString")]
    private static unsafe void* RhNewString(MethodTable* pEEType, int length)
    {
        return RhpNewArray(pEEType, length);
    }

    [RuntimeExport("RhRegisterFrozenSegment")]
    static unsafe IntPtr RhRegisterFrozenSegment(void* pSegmentStart, nuint allocSize, nuint commitSize, nuint reservedSize)
    {
        if (GarbageCollector.IsEnabled)
        {
            return GarbageCollector.RegisterFrozenSegment((IntPtr)pSegmentStart, allocSize, commitSize, reservedSize);
        }
        else
        {
            return (IntPtr)pSegmentStart;
        }
    }

    [RuntimeExport("RhUpdateFrozenSegment")]
    static unsafe void RhUpdateFrozenSegment(IntPtr seg, void* allocated, void* committed)
    {
        if (GarbageCollector.IsEnabled)
        {
            GarbageCollector.UpdateFrozenSegment((nint)seg, (nint)allocated, (nint)committed);
        }
    }

    [RuntimeExport("RhHandleSet")]
    static IntPtr RhHandleSet(object obj)
    {
        return IntPtr.Zero;
    }

    [RuntimeExport("RhHandleFree")]
    static void RhHandleFree(IntPtr handle)
    {
        GarbageCollector.FreeHandle(handle);
    }

    [RuntimeExport("RhpHandleAlloc")]
    static IntPtr RhpHandleAlloc(GCObject* obj, GCHandleType handleType)
    {
        return GarbageCollector.AllocateHandler(obj, handleType, UIntPtr.Zero);
    }

    [RuntimeExport("RhpHandleAllocDependent")]
    static IntPtr RhpHandleAllocDependent(GCObject* primary, GCObject* secondary)
    {
        return GarbageCollector.AllocateHandler(primary, GCHandleType.Normal, (nuint)secondary);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe GCObject* AllocObject(uint size, GC_ALLOC_FLAGS flags = GC_ALLOC_FLAGS.GC_ALLOC_NO_FLAGS)
    {
        if (GarbageCollector.IsEnabled)
        {
            return GarbageCollector.AllocObject((nint)size, flags);
        }
        else
        {
            var result = MemoryOp.Alloc(size);
            MemoryOp.MemSet((byte*)result, 0, (int)size);
            return (GCObject*)result;
        }
    }
}
