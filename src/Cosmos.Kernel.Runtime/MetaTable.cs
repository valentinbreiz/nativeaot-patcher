using System.Diagnostics.CodeAnalysis;
using System.Runtime;
using Internal.Runtime;

namespace Cosmos.Kernel.Runtime;

public class MetaTable
{
    // look int what this does
    [RuntimeExport("RhpGetModuleSection")]
    private static IntPtr RhGetModuleSection(ref TypeManagerHandle module, ReadyToRunSectionType section,
        out int length)
    {
        length = 0;
        return IntPtr.Zero;
    }


    [RuntimeExport("RhGetModuleFileName")]
    internal static unsafe int RhGetModuleFileName(IntPtr moduleHandle, out byte* moduleName) {
        moduleName = (byte*) 0x00;
        return 0;
    }

    [RuntimeExport("RhpCheckedXchg")]
    internal static object InterlockedExchange([NotNullIfNotNull(nameof(value))] ref object? location1, object? value)
    {
        return value;
    }

    // this is 100% wrong
    [RuntimeExport("RhpInitialDynamicInterfaceDispatch")]
    static void RhpInitialDynamicInterfaceDispatch()
    {
    }

    [RuntimeExport("RhHandleGetDependent")]
    static void RhHandleGetDependent()
    {
    }

}
