using System.Runtime.InteropServices;
using Cosmos.Build.API.Attributes;
using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Scheduler;
using Cosmos.Kernel.System.Timer;
using SysThread = System.Threading.Thread;
using SchedThread = Cosmos.Kernel.Core.Scheduler.Thread;
using Cosmos.Kernel.Core;
#if ARCH_X64
using Cosmos.Kernel.HAL.X64.Cpu;
#endif

namespace Cosmos.Kernel.Plugs.System.Threading;

[Plug(typeof(SysThread))]
public static class ThreadPlug
{
    // Store delegates indexed by thread ID
    private static readonly Dictionary<uint, ThreadStart> _threadDelegates = new();

    // Queue of pending delegates (to handle multiple thread creations)
    private static readonly Queue<ThreadStart> _pendingDelegates = new();

    [PlugMember(".ctor")]
    public static void Ctor(SysThread aThis, ThreadStart start)
    {
        Serial.WriteString("[ThreadPlug] Ctor(ThreadStart)\n");

        // Only disable interrupts if scheduler is running (to avoid issues during early boot)
        if (SchedulerManager.Enabled)
        {
            using (InternalCpu.DisableInterruptsScope())
            {
                _pendingDelegates.Enqueue(start);
            }
        }
    }

    [PlugMember(".ctor")]
    public static void Ctor(SysThread aThis, ThreadStart start, int maxStackSize)
    {
        Serial.WriteString("[ThreadPlug] Ctor(ThreadStart, maxStackSize)\n");

        // Only disable interrupts if scheduler is running (to avoid issues during early boot)
        if (SchedulerManager.Enabled)
        {
            using (InternalCpu.DisableInterruptsScope())
            {
                _pendingDelegates.Enqueue(start);
            }
        }
    }

    [PlugMember("StartCore")]
    public static unsafe void StartCore(SysThread aThis)
    {
        Serial.WriteString("[ThreadPlug] StartCore()\n");

        // Disable interrupts for thread-safe queue/dictionary access
        bool needsProtection = SchedulerManager.Enabled;

        if (SchedulerManager.Enabled)
        {
            using (InternalCpu.DisableInterruptsScope())
            {
                if (_pendingDelegates.Count == 0)
                {
                    Serial.WriteString("[ThreadPlug] No delegate found\n");
                    return;
                }

                var start = _pendingDelegates.Dequeue();

                // Create scheduler thread
                var thread = new SchedThread
                {
                    Id = SchedulerManager.AllocateThreadId(),
                    CpuId = 0,
                    State = Cosmos.Kernel.Core.Scheduler.ThreadState.Created
                };

                // Store delegate for this thread
                _threadDelegates[thread.Id] = start;

                Serial.WriteString("[ThreadPlug] Thread ");
                Serial.WriteNumber(thread.Id);
                Serial.WriteString(" - setting up stack\n");

#if ARCH_X64
                // Get code selector
                ushort cs = (ushort)Idt.GetCurrentCodeSelector();

                // Initialize stack with our entry point, passing thread ID as argument
                nuint entryPoint = (nuint)(delegate* unmanaged<void>)&ThreadEntryPoint;
                thread.InitializeStack(entryPoint, cs, thread.Id);

                Serial.WriteString("[ThreadPlug] Stack initialized, registering with scheduler\n");
#elif ARCH_ARM64
                // ARM64: no code selector needed, use 0
                nuint entryPoint = (nuint)(delegate* unmanaged<void>)&ThreadEntryPoint;
                thread.InitializeStack(entryPoint, 0, thread.Id);

                Serial.WriteString("[ThreadPlug] Stack initialized, registering with scheduler\n");
#endif

                // Register with scheduler
                SchedulerManager.CreateThread(0, thread);
                SchedulerManager.ReadyThread(0, thread);

                Serial.WriteString("[ThreadPlug] Thread ");
                Serial.WriteNumber(thread.Id);
                Serial.WriteString(" scheduled for execution\n");
            }
        }
    }

    /// <summary>
    /// Entry point for scheduled threads. Called by scheduler when thread is switched to.
    /// </summary>
    [UnmanagedCallersOnly]
    private static void ThreadEntryPoint()
    {
        // Now try to get thread info
        var cpuState = SchedulerManager.GetCpuState(0);
        var currentThread = cpuState.CurrentThread;

        if (currentThread == null)
        {
            Panic.Halt("No current thread in ThreadEntryPoint");
        }

        uint threadId = currentThread.Id;
        Serial.WriteString("[ThreadPlug] Running thread ");
        Serial.WriteNumber(threadId);
        Serial.WriteString("\n");

        int exitCode = 0;
        bool hasDelegate;
        ThreadStart? start;

        // Get the delegate with interrupts disabled
        using (InternalCpu.DisableInterruptsScope())
        {
            hasDelegate = _threadDelegates.TryGetValue(threadId, out start);
            if (hasDelegate)
                _threadDelegates.Remove(threadId);
        }

        // Invoke the delegate
        if (hasDelegate && start != null)
        {
            try
            {
                Serial.WriteString("[ThreadPlug] Invoking delegate\n");
                start.Invoke();
                Serial.WriteString("[ThreadPlug] Delegate completed\n");
            }
            catch (Exception ex)
            {
                exitCode = 1;
                // Re-query thread ID - local variables may not be accessible in catch funclet
                var exCpuState = SchedulerManager.GetCpuState(0);
                uint exThreadId = exCpuState?.CurrentThread?.Id ?? 0;
                Serial.WriteString("[ThreadPlug] Thread ");
                Serial.WriteNumber(exThreadId);
                Serial.WriteString(" threw exception: ");
                Serial.WriteString(ex.Message ?? "Unknown error");
                Serial.WriteString("\n");
            }
        }
        else
        {
            Serial.WriteString("[ThreadPlug] No delegate for thread ");
            Serial.WriteNumber(threadId);
            Serial.WriteString("\n");
        }

        // Re-query current thread for exit (local vars may be corrupted after exception)
        var exitCpuState = SchedulerManager.GetCpuState(0);
        var exitThread = exitCpuState?.CurrentThread;
        uint exitThreadId = exitThread?.Id ?? 0;

        Serial.WriteString("[ThreadPlug] Thread ");
        Serial.WriteNumber(exitThreadId);
        Serial.WriteString(" exiting with code ");
        Serial.WriteNumber((uint)exitCode);
        Serial.WriteString("\n");

        // Mark thread as exited so scheduler won't re-queue it
        if (exitThread != null)
        {
            SchedulerManager.ExitThread(0, exitThread);
        }

        // Halt forever - scheduler should not pick this thread again
        while (true)
        {
            InternalCpu.Halt();
        }
    }

    [PlugMember]
    public static void Sleep(int millisecondsTimeout)
    {
        if (millisecondsTimeout > 0)
            TimerManager.Wait((uint)millisecondsTimeout);
    }

    [PlugMember]
    public static void Sleep(TimeSpan timeout)
    {
        Sleep((int)timeout.TotalMilliseconds);
    }

    [PlugMember]
    public static bool Yield() => true;

    [PlugMember]
    public static void SpinWait(int iterations)
    {
        for (int i = 0; i < iterations; i++) { }
    }
}
