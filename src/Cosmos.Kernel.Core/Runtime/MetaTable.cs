using System.Runtime;

namespace Cosmos.Kernel.Core.Runtime;

/// <summary>
/// Contains runtime exports for various metadata and dispatch operations
/// </summary>
public class MetaTable
{
    [RuntimeExport("RhGetModuleFileName")]
    internal static unsafe int RhGetModuleFileName(IntPtr moduleHandle, out byte* moduleName)
    {
        moduleName = (byte*)0x00;
        return 0;
    }

    [RuntimeExport("RhHandleGetDependent")]
    static void RhHandleGetDependent()
    {
        // Stub implementation - GC handle operations not fully supported
    }
}
