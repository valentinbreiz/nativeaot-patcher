using System.Runtime;
using Cosmos.Kernel.Core.Scheduler;

namespace Cosmos.Kernel.Core.Runtime;

public class Thread
{
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    private static object[][] threadData;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    [RuntimeExport("RhGetThreadStaticStorage")]
    static ref object[][] RhGetThreadStaticStorage()
    {
        if (CosmosFeatures.SchedulerEnabled)
        {
            var cpuState = SchedulerManager.GetCpuState(0);
            return ref cpuState.CurrentThread!.GetThreadStaticStorage();
        }
        else
        {
            return ref threadData;
        }
    }
}
