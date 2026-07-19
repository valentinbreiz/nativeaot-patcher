using System.Diagnostics;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.Scheduler;

namespace Cosmos.Kernel.Core.Bridge.Interop;

internal static class libSystemNative
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessCpuInformation
    {
        internal ulong lastRecordedCurrentTime;
        internal ulong lastRecordedKernelTime;
        internal ulong lastRecordedUserTime;
    }

    [UnmanagedCallersOnly(EntryPoint = "SystemNative_GetCpuUtilization")]
    internal static unsafe double SystemNative_GetCpuUtilization(ProcessCpuInformation* previousCpuInfo)
    {
        if (SchedulerManager.Threads == null)
        {
            return 0.0;
        }

        ulong currentTime = GetMonotonicNs();
        ulong busyTime = SchedulerManager.GetBusyCpuTimeNs();

        ulong lastTime = previousCpuInfo->lastRecordedCurrentTime;
        ulong lastBusy = previousCpuInfo->lastRecordedUserTime;

        // First call: seed snapshot only.
        if (lastTime == 0)
        {
            previousCpuInfo->lastRecordedCurrentTime = currentTime;
            previousCpuInfo->lastRecordedUserTime = busyTime;
            previousCpuInfo->lastRecordedKernelTime = 0;
            return 0.0;
        }

        // Window too short for meaningful sample (< 5 ticks at 10 ms quantum).
        // Leave snapshot untouched so the next call sees a longer window.
        if (currentTime - lastTime < 50_000_000UL)
        {
            return 0.0;
        }

        double utilization = 0.0;
        if (busyTime >= lastBusy)
        {
            ulong totalElapsed = (currentTime - lastTime) * SchedulerManager.CpuCount;
            ulong busyElapsed = busyTime - lastBusy;
            if (totalElapsed > 0 && busyElapsed > 0)
            {
                utilization = (double)busyElapsed * 100.0 / (double)totalElapsed;
                if (utilization > 100.0)
                {
                    utilization = 100.0;
                }
            }
        }

        previousCpuInfo->lastRecordedCurrentTime = currentTime;
        previousCpuInfo->lastRecordedUserTime = busyTime;
        previousCpuInfo->lastRecordedKernelTime = 0;
        return utilization;
    }

    [UnmanagedCallersOnly(EntryPoint = "SystemNative_SchedGetCpu")]
    internal static int SystemNative_SchedGetCpu()
    {
        return (int)SchedulerManager.GetCurrentCpuId();
    }

    private static ulong GetMonotonicNs()
    {
        long ticks = Stopwatch.GetTimestamp();
        long freq = Stopwatch.Frequency;
        if (freq <= 0)
        {
            return 0;
        }
        ulong t = (ulong)ticks;
        ulong f = (ulong)freq;
        // Split mul/div: ticks * 1e9 overflows in ~147 s on a 62.5 MHz ARM64 timer.
        return (t / f) * 1_000_000_000UL + ((t % f) * 1_000_000_000UL) / f;
    }
}
