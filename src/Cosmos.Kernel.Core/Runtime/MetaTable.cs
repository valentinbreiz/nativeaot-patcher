using System.Diagnostics.CodeAnalysis;
using System.Runtime;

namespace Cosmos.Kernel.Core.Runtime;

public class MetaTable
{


    [RuntimeExport("RhGetModuleFileName")]
    internal static unsafe int RhGetModuleFileName(IntPtr moduleHandle, out byte* moduleName)
    {
        moduleName = (byte*)0x00;
        return 0;
    }


    // this is 100% wrong
    [RuntimeExport("RhpInitialDynamicInterfaceDispatch")]
    static void RhpInitialDynamicInterfaceDispatch()
    {
    }

    // RhHandleGetDependent moved to Stdllib.cs with proper implementation
}
