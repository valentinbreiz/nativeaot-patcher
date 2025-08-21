using System.Runtime;
using Cosmos.Kernel.Core.Memory;

namespace Cosmos.Kernel.Runtime;

public static class Memory
{
    [RuntimeExport("RhAllocateNewArray")]
    internal static unsafe void RhAllocateNewArray(MethodTable* pArrayEEType, uint numElements, uint flags,
        out void* pResult)
    {
        uint size = pArrayEEType->_uBaseSize * numElements;
        pResult = MemoryOp.Alloc(size);
        // as some point we should set flags
    }

    [RuntimeExport("RhpNewArray")]
    private static unsafe void* RhpNewArray(MethodTable* pMT, int length)
    {
        if (length < 0)
            return null;

        uint size = pMT->_uBaseSize + (uint)length * pMT->_usComponentSize;
        MethodTable** result = AllocObject(size);
        *result = pMT;
        *(int*)(result + 1) = length;
        return result;
    }

    [RuntimeExport("RhpNewFast")]
    private static unsafe void* RhpNewFast(MethodTable* pMT)
    {
        MethodTable** result = AllocObject(pMT->_uBaseSize);
        *result = pMT;
        return result;
    }

    private static unsafe MethodTable** AllocObject(uint size)
    {
        return (MethodTable**)MemoryOp.Alloc(size);
    }

    private static unsafe MethodTable* GetMethodTable(object obj)
    {
        TypedReference tr = __makeref(obj);
        return (MethodTable*)*(IntPtr*)&tr;
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
