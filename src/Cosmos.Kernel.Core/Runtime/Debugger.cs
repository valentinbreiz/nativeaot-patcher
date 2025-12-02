using System.Runtime;

namespace Cosmos.Kernel.Core.Runtime;

internal static class Debugger
{
    [RuntimeExport("DebugDebugger_IsNativeDebuggerAttached")]
    internal static int DebugDebugger_IsNativeDebuggerAttached()
    {
        return 1;
    }
}
