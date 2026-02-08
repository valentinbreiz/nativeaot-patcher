using System.Runtime;
using Cosmos.Kernel.Core.Scheduler;

namespace Cosmos.Kernel.Core.Runtime;

public class Thread
{
    [RuntimeExport("RhGetThreadStaticStorage")]
    static ref object[][] RhGetThreadStaticStorage()
    {
        var cpuState = SchedulerManager.GetCpuState(0);
        return ref cpuState.CurrentThread!.GetThreadStaticStorage();
    }
}
