using System;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Scheduler;
using Cosmos.Kernel.System.Timer;
using DevKernel.Diagnostics;
using DevKernel.Shell;
using SchedThread = Cosmos.Kernel.Core.Scheduler.Thread;
using SchedThreadState = Cosmos.Kernel.Core.Scheduler.ThreadState;
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

    /// <summary>Thread ID of the scheduler's idle thread, which the kill command must refuse to kill.</summary>
    private const uint IdleThreadId = 0;

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

        IScheduler? scheduler = SchedulerManager.Current;
        if (scheduler == null)
        {
            Terminal.InfoLine("Status", "Not initialized");
            return;
        }

        Terminal.StatusLine(
            "Status",
            SchedulerManager.Enabled ? "ENABLED" : "DISABLED",
            SchedulerManager.Enabled ? ConsoleColor.Green : ConsoleColor.Red);

        Terminal.InfoLine("Scheduler", scheduler.Name);
        Terminal.InfoLine("CPU Count", SchedulerManager.CpuCount.ToString());
        Terminal.InfoLine("Quantum", (SchedulerManager.DefaultQuantumNs / Units.NsPerMs).ToString() + " ms");
        Console.WriteLine();

        for (uint cpuId = 0; cpuId < SchedulerManager.CpuCount; cpuId++)
        {
            PerCpuState cpuState = SchedulerManager.GetCpuState(cpuId);

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  CPU " + cpuId + ":");
            Console.ResetColor();

            SchedThread? currentThread = cpuState.CurrentThread;
            if (currentThread != null)
            {
                PrintThreadInfo(scheduler, currentThread);
            }

            int runQueueCount = scheduler.GetRunQueueCount(cpuState);
            for (int i = 0; i < runQueueCount; i++)
            {
                SchedThread? thread = scheduler.GetRunQueueThread(cpuState, i);
                if (thread != null)
                {
                    PrintThreadInfo(scheduler, thread);
                }
            }
        }

        Console.WriteLine();
    }

    private static void PrintThreadInfo(IScheduler scheduler, SchedThread thread)
    {
        Console.Write("    ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write("Thread " + thread.Id);

        Console.Write(" ");
        switch (thread.State)
        {
            case SchedThreadState.Running:
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("Running");
                break;
            case SchedThreadState.Ready:
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("Ready");
                break;
            case SchedThreadState.Blocked:
            case SchedThreadState.Sleeping:
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write(thread.State == SchedThreadState.Blocked ? "Blocked" : "Sleeping");
                break;
            case SchedThreadState.Dead:
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("Dead");
                break;
            default:
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.Write("Unknown");
                break;
        }

        if (thread.SchedulerData != null)
        {
            long priority = scheduler.GetPriority(thread);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(" Pri=" + priority);
        }

        ulong runtimeMs = thread.TotalRuntime / Units.NsPerMs;
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write(" Run=" + runtimeMs + "ms");

        Console.ResetColor();
        Console.WriteLine();
    }

    private static void TestThread()
    {
        Serial.WriteString("[Thread] Testing System.Threading.Thread API\n");
        Terminal.Info("Creating and starting a thread...");

        SysThread thread = new(static () =>
        {
            Serial.WriteString("[Thread] Hello from thread delegate!\n");
            Console.WriteLine("Hello from thread!");
        });

        thread.Start();
        Terminal.Success("Thread started!");
        Console.WriteLine();

        TimerManager.Wait(ThreadTestWaitMs);
    }

    private static void KillThread(uint threadId)
    {
        IScheduler? scheduler = SchedulerManager.Current;
        if (scheduler == null)
        {
            Terminal.Error("Scheduler not initialized");
            return;
        }

        if (threadId == IdleThreadId)
        {
            Terminal.Error("Cannot kill idle thread (ID " + IdleThreadId + ")");
            return;
        }

        for (uint cpuId = 0; cpuId < SchedulerManager.CpuCount; cpuId++)
        {
            PerCpuState cpuState = SchedulerManager.GetCpuState(cpuId);

            if (cpuState.CurrentThread?.Id == threadId)
            {
                Terminal.Warning("Cannot kill currently running thread");
                cpuState.CurrentThread.State = SchedThreadState.Dead;
                return;
            }

            int count = scheduler.GetRunQueueCount(cpuState);
            for (int i = 0; i < count; i++)
            {
                SchedThread? thread = scheduler.GetRunQueueThread(cpuState, i);
                if (thread?.Id == threadId)
                {
                    SchedulerManager.ExitThread(cpuId, thread);
                    Terminal.Success("Thread " + threadId + " killed");
                    Console.WriteLine();
                    return;
                }
            }
        }

        Terminal.Error("Thread " + threadId + " not found");
    }
}
