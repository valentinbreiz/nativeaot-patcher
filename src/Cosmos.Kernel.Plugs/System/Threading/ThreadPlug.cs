using Cosmos.Build.API.Attributes;
using Cosmos.Kernel.Core.Bridge;
using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Scheduler;
using Cosmos.Kernel.System.Timer;
using SysThread = System.Threading.Thread;
using SchedThread = Cosmos.Kernel.Core.Scheduler.Thread;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;


#if ARCH_X64
using Cosmos.Kernel.Core.X64.Cpu;
#endif

namespace Cosmos.Kernel.Plugs.System.Threading;

[Plug(typeof(SysThread))]
public static unsafe class ThreadPlug
{
    [PlugMember]
    public static bool CreateThread(SysThread aThis, GCHandle<SysThread> thisThreadHandle)
    {
        Serial.WriteString("[ThreadPlug] CreateThread(GCHandle<Thread>)\n");

        // Initialize the '_stopped' field
        _stopped(aThis) = new ManualResetEvent(false);

        if (SchedulerManager.Enabled)
        {
            using (InternalCpu.DisableInterruptsScope())
            {
                // Create scheduler thread with the ThreadFlags.Managed set.
                // SchedulerManager.InvokeCurrentThreadStart evaluates it to
                // call the managed startup or not.
                SchedThread thread = new SchedThread
                {
                    Id = SchedulerManager.AllocateThreadId(),
                    CpuId = 0,
                    State = Cosmos.Kernel.Core.Scheduler.ThreadState.Created,
                    Flags = ThreadFlags.Managed
                };

                Serial.WriteString("[ThreadPlug] Thread ");
                Serial.WriteNumber(thread.Id);
                Serial.WriteString(" - setting up stack\n");

                // Initial RIP/PC is the stable native entry-point stub in Core;
                // the scheduler's InvokeCurrentThreadStart runs the registered delegate.
                nuint entryPoint = (nuint)(delegate* unmanaged<IntPtr, void>)&ThreadNative.EntryPointStub;
#if ARCH_X64
                ushort cs = (ushort)Idt.GetCurrentCodeSelector();
                thread.InitializeStack(entryPoint, cs, (nuint)GCHandle<SysThread>.ToIntPtr(thisThreadHandle));
#elif ARCH_ARM64
                // ARM64: no code selector needed, use 0.
                thread.InitializeStack(entryPoint, 0, (nuint)GCHandle<SysThread>.ToIntPtr(thisThreadHandle));
#endif
                Serial.WriteString("[ThreadPlug] Stack initialized, registering with scheduler\n");

                SchedulerManager.CreateThread(0, thread);
                SchedulerManager.ReadyThread(0, thread);

                Serial.WriteString("[ThreadPlug] Thread ");
                Serial.WriteNumber(thread.Id);
                Serial.WriteString(" scheduled for execution\n");
            }
        }

        return true;
    }

    // TODO: Implement RhYield
    [PlugMember]
    public static bool Yield() => true;

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_stopped")]
    private static extern ref ManualResetEvent _stopped(SysThread aThis);
}
