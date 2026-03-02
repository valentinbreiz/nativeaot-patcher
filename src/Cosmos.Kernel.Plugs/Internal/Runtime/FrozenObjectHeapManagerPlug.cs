using Cosmos.Build.API.Attributes;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.Core.Memory.Heap;
using Cosmos.Kernel.Core.Runtime;

namespace Cosmos.Kernel.Plugs.Internal.Runtime;

[Plug(TargetName = "Internal.Runtime.FrozenObjectHeapManager")]
internal unsafe partial class FrozenObjectHeapManagerPlug
{
    /// <summary>
    /// Reserves a Heap block of the specified size.
    /// </summary>
    [PlugMember]
    private static void* ClrVirtualReserve(nuint size)
    {
        return Heap.Alloc((uint)size);
    }

    /// <summary>
    /// Commits a Heap block of the specified size.
    /// </summary>
    [PlugMember]
    private static void* ClrVirtualCommit(void* pBase, nuint size)
    {
        return pBase;
    }

    /// <summary>
    /// Frees a previously reserved Heap block.
    /// </summary>
    /// <param name="pBase"></param>
    /// <param name="size"></param>
    [PlugMember]
    private static void ClrVirtualFree(void* pBase, nuint size)
    {
        Heap.Free(pBase);
    }
}
