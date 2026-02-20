using System.Runtime;
using System.Runtime.CompilerServices;
using Cosmos.Kernel.Core.Memory.GarbageCollector;

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
}
