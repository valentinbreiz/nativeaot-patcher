using System;
using Cosmos.Kernel.System.Diagnostics;
using Cosmos.Kernel.System.Timer;
using DevKernel.Diagnostics;
using DevKernel.Shell;
using SysThread = System.Threading.Thread;

namespace DevKernel.Commands;

/// <summary>
/// Scheduler introspection, plus the managed-thread smoke tests.
/// </summary>
internal static class SchedulerCommands
{
    /// <summary>Help section these commands are listed under.</summary>
    private const string Category = "Scheduler";

    /// <summary>Delay (ms) after starting the test thread so its output can appear.</summary>
    private const uint ThreadTestWaitMs = 2000;

    public static void Register(CommandShell shell)
    {
        shell.Register(
            Category,
            new ShellCommand
            {
                Name = "schedinfo",
                Usage = "schedinfo",
                Description = "Show scheduler status and threads",
                Execute = static (context, args) => ShowSchedulerInfo(),
            },
            new ShellCommand
            {
                Name = "thread",
                Usage = "thread",
                Description = "Test System.Threading.Thread",
                Execute = static (context, args) => TestThread(),
            },
            new ShellCommand
            {
                Name = "kill",
                Usage = "kill <thread_id>",
                Description = "Kill a thread by ID",
                MinArgs = 1,
                MaxArgs = 1,
                Execute = static (context, args) =>
                {
                    if (!args.TryGetUInt(0, out uint threadId))
                    {
                        args.PrintUsage();
                        return;
                    }

                    KillThread(threadId);
                },
            },
            new ShellCommand
            {
                Name = "cpustat",
                Usage = "cpustat",
                Description = "Live CPU% + thread monitor with stress wave",
                Execute = static (context, args) => CpuStat.Run(),
            });
    }

    private static void ShowSchedulerInfo()
    {
        Terminal.Header("Scheduler Information:");

        if (!SchedulerInfo.IsInitialized)
        {
            Terminal.InfoLine("Status", "Not initialized");
            return;
        }

        Terminal.StatusLine(
            "Status",
            SchedulerInfo.IsRunning ? "ENABLED" : "DISABLED",
            SchedulerInfo.IsRunning ? ConsoleColor.Green : ConsoleColor.Red);

        Terminal.InfoLine("Scheduler", SchedulerInfo.SchedulerName!);
        Terminal.InfoLine("CPU Count", SchedulerInfo.CpuCount.ToString());
        Terminal.InfoLine("Quantum", (SchedulerInfo.QuantumNs / Units.NsPerMs).ToString() + " ms");
        Console.WriteLine();

        for (uint cpuId = 0; cpuId < SchedulerInfo.CpuCount; cpuId++)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  CPU " + cpuId + ":");
            Console.ResetColor();

            if (SchedulerInfo.TryGetCurrentThread(cpuId, out KernelThreadInfo currentThread))
            {
                PrintThreadInfo(currentThread);
            }

            int runQueueCount = SchedulerInfo.GetRunQueueCount(cpuId);
            for (int i = 0; i < runQueueCount; i++)
            {
                if (SchedulerInfo.TryGetRunQueueThread(cpuId, i, out KernelThreadInfo thread))
                {
                    PrintThreadInfo(thread);
                }
            }
        }

        Console.WriteLine();
    }

    private static void PrintThreadInfo(KernelThreadInfo info)
    {
        Console.Write("    ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write("Thread " + info.Id);

        Console.Write(" ");
        switch (info.State)
        {
            case KernelThreadState.Running:
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("Running");
                break;
            case KernelThreadState.Ready:
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("Ready");
                break;
            case KernelThreadState.Blocked:
            case KernelThreadState.Sleeping:
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write(info.State == KernelThreadState.Blocked ? "Blocked" : "Sleeping");
                break;
            case KernelThreadState.Dead:
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("Dead");
                break;
            default:
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.Write("Unknown");
                break;
        }

        if (info.HasPriority)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(" Pri=" + info.Priority);
        }

        ulong runtimeMs = info.TotalRuntimeNs / Units.NsPerMs;
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write(" Run=" + runtimeMs + "ms");

        Console.ResetColor();
        Console.WriteLine();
    }

    private static void TestThread()
    {
        Log.WriteString("[Thread] Testing System.Threading.Thread API\n");
        Terminal.Info("Creating and starting a thread...");

        SysThread thread = new(static () =>
        {
            Log.WriteString("[Thread] Hello from thread delegate!\n");
            Console.WriteLine("Hello from thread!");
        });

        thread.Start();
        Terminal.Success("Thread started!");
        Console.WriteLine();

        TimerManager.Wait(ThreadTestWaitMs);
    }

    private static void KillThread(uint threadId)
    {
        if (!SchedulerInfo.IsInitialized)
        {
            Terminal.Error("Scheduler not initialized");
            return;
        }

        switch (SchedulerInfo.RequestKill(threadId))
        {
            case ThreadKillResult.Killed:
                Terminal.Success("Thread " + threadId + " killed");
                Console.WriteLine();
                break;
            case ThreadKillResult.MarkedForExit:
                Terminal.Warning("Thread " + threadId + " is running; marked for exit at its next reschedule");
                break;
            case ThreadKillResult.RefusedBlocked:
                Terminal.Error("Thread " + threadId + " is blocked or sleeping; wake it before killing it");
                break;
            case ThreadKillResult.RefusedIdle:
                Terminal.Error("Cannot kill idle thread " + threadId);
                break;
            default:
                Terminal.Error("Thread " + threadId + " not found");
                break;
        }
    }
}
