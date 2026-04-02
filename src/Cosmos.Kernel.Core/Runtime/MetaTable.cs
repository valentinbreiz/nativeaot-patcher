using System.Runtime;
using System.Runtime.CompilerServices;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory.GarbageCollector;
using Internal.Runtime;

namespace Cosmos.Kernel.Core.Runtime;

/// <summary>
/// Contains runtime exports for various metadata and dispatch operations
/// </summary>
public unsafe class MetaTable
{
    [RuntimeExport("RhGetModuleFileName")]
    internal static int RhGetModuleFileName(IntPtr moduleHandle, out byte* moduleName)
    {
        moduleName = (byte*)0x00;
        return 0;
    }

    [RuntimeExport("RhHandleGetDependent")]
    static GCObject* RhHandleGetDependent(IntPtr handle, out GCObject* pSecondary)
    {
        GCObject* primary = GarbageCollector.HandleGetPrimary(handle);
        if (primary != null)
        {
            pSecondary = GarbageCollector.HandleGetSecondary(handle);
        }
        else
        {
            pSecondary = null;
        }

        return primary;
    }

    [RuntimeExport("RhHandleSetDependentSecondary")]
    static void RhHandleSetDependentSecondary(IntPtr handle, GCObject* pSecondary)
    {
        GarbageCollector.HandleSetSecondary(handle, pSecondary);
    }

    [RuntimeExport("RhGetRuntimeHelperForType")]
    internal static unsafe IntPtr RhGetRuntimeHelperForType(MethodTable* pEEType, RuntimeHelperKind kind)
    {
        return __RhGetRuntimeHelperForType(null!, (IntPtr)pEEType, kind);
    }

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "RhGetRuntimeHelperForType")]
    private static extern IntPtr __RhGetRuntimeHelperForType([UnsafeAccessorType("System.Runtime.RuntimeExports")] object @this
            , [UnsafeAccessorType("Internal.Runtime.MethodTable*")] object pEEType
            , [UnsafeAccessorType("Internal.Runtime.RuntimeHelperKind")] object kind);
}
