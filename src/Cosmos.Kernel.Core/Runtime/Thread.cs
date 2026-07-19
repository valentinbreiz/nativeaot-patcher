using System.Runtime;
using System.Runtime.InteropServices.Marshalling;
using Cosmos.Kernel.Core.Bridge;
using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Scheduler;

namespace Cosmos.Kernel.Core.Runtime;

public class Thread
{
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    private static object[][] threadData;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    [RuntimeExport("RhGetThreadStaticStorage")]
    internal static ref object[][] RhGetThreadStaticStorage()
    {
        if (CosmosFeatures.SchedulerEnabled)
        {
            var cpuState = SchedulerManager.GetCpuState(SchedulerManager.GetCurrentCpuId());
            return ref cpuState.CurrentThread!.GetThreadStaticStorage();
        }
        else
        {
            return ref threadData;
        }
    }

    [RuntimeExport("RhGetCurrentThreadStackBounds")]
    internal static void RhGetCurrentThreadStackBounds(out IntPtr pStackLow, out IntPtr pStackHigh)
    {
        pStackLow = (nint)ContextSwitchNative.GetSp();
        pStackHigh = pStackLow + (nint)Scheduler.Thread.DefaultStackSize;
    }

    [RuntimeExport("RhSetCurrentThreadName")]
    internal static unsafe void RhSetCurrentThreadName(ushort* name)
    {
        // Do nothing, the managed thread holds the string on a field.
        var managedName = Utf8StringMarshaller.ConvertToManaged((byte*)name);

        Serial.WriteString($"[Thread] Setting current thread name to '{managedName}'\n");
    }

    [RuntimeExport("RhSetThreadExitCallback")]
    internal static void RhSetThreadExitCallback(IntPtr callback)
    {
        if (CosmosFeatures.SchedulerEnabled)
        {
            SchedulerManager.OnThreadExitCallback = callback;
        }
    }

    [RuntimeExport("RhYield")]
    internal static int RhYield()
    {
        Serial.WriteString("RhYield Called\n");
        if (CosmosFeatures.SchedulerEnabled)
        {
            Scheduler.Thread? thread = SchedulerManager.GetCpuState(SchedulerManager.GetCurrentCpuId()).CurrentThread;
            if (thread != null)
            {
                //TODO: Switch Threads (if possible)
                SchedulerManager.YieldThread(SchedulerManager.GetCurrentCpuId(), thread);
                InternalCpu.Halt();

                return 0;
            }
        }

        return 0;
    }

    [RuntimeExport("RhSpinWait")]
    internal static void RhSpinWait(int iterations)
    {
        // Simple spin wait
        for (int i = 0; i < iterations; i++)
        {
            // Spin
        }
    }
}
