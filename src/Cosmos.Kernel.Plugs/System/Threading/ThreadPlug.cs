using System.Collections.Generic;
using System.Runtime.InteropServices;
using Cosmos.Build.API.Attributes;
using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Scheduler;
using Cosmos.Kernel.Services.Timer;
using SysThread = System.Threading.Thread;
using SchedThread = Cosmos.Kernel.Core.Scheduler.Thread;
#if ARCH_X64
using Cosmos.Kernel.HAL.X64.Cpu;
#endif

namespace Cosmos.Kernel.Plugs.System.Threading;

[Plug(typeof(SysThread))]
public static class ThreadPlug
{
    // Store delegates indexed by thread ID
    private static readonly Dictionary<uint, ThreadStart> _threadDelegates = new();

    // Store the last delegate from constructor (for linking to thread ID in StartCore)
    private static ThreadStart? _pendingDelegate;

    [PlugMember(".ctor")]
    public static void Ctor(SysThread aThis, ThreadStart start)
    {
        Serial.WriteString("[ThreadPlug] Ctor(ThreadStart)\n");
        _pendingDelegate = start;
    }

    [PlugMember(".ctor")]
    public static void Ctor(SysThread aThis, ThreadStart start, int maxStackSize)
    {
        Serial.WriteString("[ThreadPlug] Ctor(ThreadStart, maxStackSize)\n");
        _pendingDelegate = start;
    }

    [PlugMember("StartCore")]
    public static unsafe void StartCore(SysThread aThis)
    {
        Serial.WriteString("[ThreadPlug] StartCore()\n");

        if (_pendingDelegate == null)
        {
            Serial.WriteString("[ThreadPlug] No delegate found\n");
            return;
        }

        var start = _pendingDelegate;
        _pendingDelegate = null;

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
        // Disable interrupts during critical thread setup to prevent
        // timer interrupt from firing during initialization
        InternalCpu.DisableInterrupts();

        // Get code selector
        ushort cs = (ushort)Idt.GetCurrentCodeSelector();

        // Initialize stack with our entry point, passing thread ID as argument
        nuint entryPoint = (nuint)(delegate* unmanaged<void>)&ThreadEntryPoint;
        thread.InitializeStack(entryPoint, cs, thread.Id);

        Serial.WriteString("[ThreadPlug] Stack initialized, registering with scheduler\n");

        // Register with scheduler
        SchedulerManager.CreateThread(0, thread);
        SchedulerManager.ReadyThread(0, thread);

        // Re-enable interrupts after thread is fully registered
        InternalCpu.EnableInterrupts();
#else
        // Register with scheduler
        SchedulerManager.CreateThread(0, thread);
        SchedulerManager.ReadyThread(0, thread);
#endif

        Serial.WriteString("[ThreadPlug] Thread ");
        Serial.WriteNumber(thread.Id);
        Serial.WriteString(" scheduled for execution\n");
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
            Serial.WriteString("[ThreadPlug] No current thread!\n");
            while (true) { }
        }

        uint threadId = currentThread.Id;
        Serial.WriteString("[ThreadPlug] Running thread ");
        Serial.WriteNumber(threadId);
        Serial.WriteString("\n");

        int exitCode = 0;

        // Get and invoke the delegate
        if (_threadDelegates.TryGetValue(threadId, out var start))
        {
            _threadDelegates.Remove(threadId);

            try
            {
                Serial.WriteString("[ThreadPlug] Invoking delegate\n");
                start.Invoke();
                Serial.WriteString("[ThreadPlug] Delegate completed\n");
            }
            catch (Exception ex)
            {
                exitCode = 1;
                Serial.WriteString("[ThreadPlug] Thread ");
                Serial.WriteNumber(threadId);
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

        Serial.WriteString("[ThreadPlug] Thread ");
        Serial.WriteNumber(threadId);
        Serial.WriteString(" exiting with code ");
        Serial.WriteNumber((uint)exitCode);
        Serial.WriteString("\n");

        // Mark thread as exited so scheduler won't re-queue it
        SchedulerManager.ExitThread(0, currentThread);

        // Halt forever - scheduler should not pick this thread again
        while (true)
        {
            Cosmos.Kernel.HAL.PlatformHAL.CpuOps?.Halt();
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
