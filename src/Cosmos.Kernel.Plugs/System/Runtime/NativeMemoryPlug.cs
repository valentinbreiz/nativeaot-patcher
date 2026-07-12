
using System.Runtime.InteropServices;
using Cosmos.Build.API.Attributes;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.Core.Memory.Heap;

namespace Cosmos.Kernel.Plugs.System.Runtime;

[Plug(typeof(NativeMemory))]
public static unsafe partial class NativeMemoryPlug
{
    [PlugMember]
    public static void* AlignedAlloc(nuint byteCount, nuint alignment)
    {
        return Alloc(byteCount);
    }

    [PlugMember]
    public static void AlignedFree(void* ptr)
    {
        MemoryOp.Free(ptr);
    }

    [PlugMember]
    public static void* AlignedRealloc(void* ptr, nuint byteCount, nuint alignment)
    {
        return Realloc(ptr, byteCount);
    }

    [PlugMember]
    public static void* Alloc(nuint byteCount)
    {
        return MemoryOp.Alloc((uint)byteCount);
    }

    [PlugMember]
    public static void* AllocZeroed(nuint elementCount, nuint elementSize)
    {
        void* result;

        if ((elementCount != 0) && (elementSize != 0))
        {
            result = MemoryOp.Alloc((uint)(elementCount * elementSize));
            MemoryOp.MemSet((byte*)result, 0, (int)(elementCount * elementSize));
        }
        else
        {
            // The C standard does not define what happens when num == 0 or size == 0, we want an "empty" allocation
            result = MemoryOp.Alloc(1);
        }

        if (result == null)
        {
            throw new OutOfMemoryException();
        }

        return result;
    }

    [PlugMember]
    public static void Free(void* ptr)
    {
        MemoryOp.Free(ptr);
    }

    [PlugMember]
    public static void* Realloc(void* ptr, nuint byteCount)
    {
        void* result = Heap.Realloc((byte*)ptr, (uint)byteCount);

        if (result == null)
        {
            throw new OutOfMemoryException();
        }

        return result;
    }
}
