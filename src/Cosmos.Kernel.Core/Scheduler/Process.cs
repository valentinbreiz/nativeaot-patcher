using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory.VAS;

namespace Cosmos.Kernel.Core.Scheduler;

/// <summary>
/// A ring-0 / EL1 user-space process. The process itself does not execute;
/// execution happens through its threads. The caller is responsible for building
/// the address space, mapping memory, and creating threads.
/// </summary>
public class Process
{
    /// <summary>
    /// Unique process identifier.
    /// </summary>
    public ulong Id { get; internal set; }

    /// <summary>
    /// Virtual address space shared by all threads in this process.
    /// </summary>
    public AddressSpace? AddressSpace { get; set; }

    /// <summary>
    /// Privilege level the process threads run at. 0 = kernel / ring 0 / EL1.
    /// 3 = user / ring 3 / EL0. Only ring 0 is supported
    /// all threads started by this process must be in the same ring.
    /// </summary>
    public byte Ring { get; set; }

    /// <summary>
    /// Threads belonging to this process.
    /// </summary>
    public List<Thread> Threads { get; } = new List<Thread>();

    /// <summary>
    /// Process state.
    /// </summary>
    public ProcessState State { get; set; }

    /// <summary>
    /// Exit code when the process terminates.
    /// </summary>
    public int ExitCode { get; private set; }

    public void StartThread(Thread thread)
    {
        thread.AddressSpace = AddressSpace;
        Threads.Add(thread);
    }

    public void Kill(int exitCode)
    {
        ExitCode = exitCode;
        State = ProcessState.Dead;

        Serial.WriteString("[ProcessManager] Process ");
        Serial.WriteNumber(Id);
        Serial.WriteString(" terminated with code ");
        Serial.WriteNumber((uint)exitCode);
        Serial.WriteString("\n");

        foreach (Thread thread in Threads)
        {
            SchedulerManager.ExitThread(thread.CpuId, thread); // kill them all
        }

        AddressSpace?.ReleaseReference();
        AddressSpace = null;

    }

}



/// <summary>
/// Lifecycle state of a process.
/// </summary>
public enum ProcessState : byte
{
    /// <summary>Process is being constructed.</summary>
    Created,

    /// <summary>Process is running (has at least one runnable thread).</summary>
    Running,

    /// <summary>Process is being terminated.</summary>
    Dying,

    /// <summary>Process has exited.</summary>
    Dead
}
