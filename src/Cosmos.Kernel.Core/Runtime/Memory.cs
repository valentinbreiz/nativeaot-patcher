using System.Runtime;
using System.Runtime.CompilerServices;
using Cosmos.Kernel.Core.Memory;
using Internal.Runtime;

namespace Cosmos.Kernel.Core.Runtime;

public static class Memory
{
    [RuntimeExport("RhAllocateNewArray")]
    internal static unsafe void RhAllocateNewArray(MethodTable* pArrayEEType, uint numElements, uint flags,
        out void* pResult)
    {
        uint size = pArrayEEType->BaseSize * numElements;
        MethodTable** result = AllocObject(size);
        *result = pArrayEEType;
        *(int*)(result + 1) = (int)numElements;
        pResult = result;
        // as some point we should set flags
    }

    [RuntimeExport("RhpNewArray")]
    internal static unsafe void* RhpNewArray(MethodTable* pMT, int length)
    {
        if (length < 0)
            return null;

        uint size = pMT->BaseSize + (uint)length * pMT->ComponentSize;
        MethodTable** result = AllocObject(size);

        *result = pMT;
        *(int*)(result + 1) = length;
        return result;
    }

    [RuntimeExport("RhpNewPtrArrayFast")]
    internal static unsafe void* RhpNewPtrArrayFast(MethodTable* pMT, int length)
    {
        if (length < 0)
            return null;

        uint size = pMT->BaseSize + (uint)length * pMT->ComponentSize;
        MethodTable** result = AllocObject(size);

        *result = pMT;
        *(int*)(result + 1) = length;
        return result;
    }
    [RuntimeExport("RhpNewArrayFast")]
    internal static unsafe void* RhpNewArrayFast(MethodTable* pMT, int length)
    {
        if (length < 0)
            return null;

        uint size = pMT->BaseSize + (uint)length * pMT->ComponentSize;
        MethodTable** result = AllocObject(size);

        *result = pMT;
        *(int*)(result + 1) = length;
        return result;
    }

    [RuntimeExport("RhNewVariableSizeObject")]
    internal static unsafe void* RhNewVariableSizeObject(MethodTable* pEEType, int length)
    {
        return RhpNewArray(pEEType, length);
    }

    [RuntimeExport("RhAllocateNewObject")]
    internal static unsafe void RhAllocateNewObject(MethodTable* pEEType, uint flags, void* pResult)
    {
        *(void**)pResult = RhpNewFast(pEEType);
        // as some point we should set flags   
    }

    [RuntimeExport("RhpGcSafeZeroMemory")]
    internal static unsafe byte* RhpGcSafeZeroMemory(byte* dmem, nuint size)
    {
        return dmem;
    }

    // Size of the sync block header (4 bytes) that sits before the MethodTable pointer
    private const int SyncBlockHeaderSize = sizeof(int);

    [RuntimeExport("RhpNewFast")]
    internal static unsafe void* RhpNewFast(MethodTable* pMT)
    {
        // Use RawBaseSize instead of BaseSize because BaseSize only works for canonical/array types
        // For generic type definitions, BaseSize contains parameter count, not the actual size
        MethodTable** result = AllocObject(pMT->RawBaseSize);
        *result = pMT;
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe MethodTable** AllocObject(uint size)
    {
        // .NET object layout requires space for the sync block header BEFORE the object reference
        // Layout: [sync block header (4 bytes)] [MethodTable* (8 bytes)] [object fields...]
        //                                       ^ object reference points here
        byte* memory = (byte*)MemoryOp.Alloc(size + SyncBlockHeaderSize);

        // Zero the sync block header (required for GetHashCode to work correctly)
        *(int*)memory = 0;

        // Return pointer to the MethodTable slot (after the sync block header)
        return (MethodTable**)(memory + SyncBlockHeaderSize);
    }

    internal static unsafe MethodTable* GetMethodTable(object obj)
    {
        // The MethodTable pointer is stored at the beginning of every object
        // RhpNewFast sets: *result = pMT; where result is the allocated address
        return *(MethodTable**)Unsafe.AsPointer(ref obj);
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
        return IntPtr.Zero;
    }

    [RuntimeExport("RhUpdateFrozenSegment")]
    static unsafe void RhUpdateFrozenSegment(IntPtr seg, void* allocated, void* committed)
    {
    }
}
