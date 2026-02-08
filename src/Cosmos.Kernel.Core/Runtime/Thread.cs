using System.Runtime;
using Cosmos.Kernel.Core.Scheduler;

namespace Cosmos.Kernel.Core.Runtime;

public class Thread
{
    private static object[][] threadData = new object[][] { };
    [RuntimeExport("RhGetThreadStaticStorage")]
    static ref object[][] RhGetThreadStaticStorage()
    {
        return ref threadData;
    }

    [RuntimeExport("RhSetCurrentThreadName")]
    internal static void RhSetCurrentThreadName(string name)
    {


    }

    [RuntimeExport("RhGetCurrentThreadStackBounds")]
    internal static void RhGetCurrentThreadStackBounds(out IntPtr pStackLow, out IntPtr pStackHigh)
    {
        pStackLow = (nint)ContextSwitch.GetRsp(); ;
        pStackHigh = pStackLow + (nint)Scheduler.Thread.DefaultStackSize;
    }
}
