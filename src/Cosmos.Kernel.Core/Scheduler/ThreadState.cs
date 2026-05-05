namespace Cosmos.Kernel.Core.Scheduler;

/// <summary>
/// Thread execution state.
/// </summary>
public enum ThreadState : byte
{
    Created,    // Just created, not yet scheduled
    Ready,      // Can be scheduled
    Running,    // Currently executing on a CPU
    Blocked,    // Waiting for I/O, lock, etc.
    Sleeping,   // Timed wait
    Dead        // Terminated, awaiting cleanup
}

/// <summary>
/// Thread flags.
/// </summary>
[Flags]
public enum ThreadFlags : ushort
{
    /// <summary>
    /// No flags set
    /// </summary>
    None = 0,
    /// <summary>
    /// Kernel-mode thread
    /// </summary>
    KernelThread = 1 << 0,
    /// <summary>
    /// Per-CPU idle thread
    /// </summary>
    IdleThread = 1 << 1,
    /// <summary>
    /// Cannot migrate to other CPUs
    /// </summary>
    Pinned = 1 << 2,
    /// <summary>
    /// Entrypoint parameter is a <see cref="System.Runtime.InteropServices.GCHandle"/> of <see cref="System.Threading.Thread"/>, 
    /// when set tells the <see cref="SchedulerManager.InvokeCurrentThreadStart"/>
    /// to call the managed thread start.
    /// </summary>
    Managed = 1 << 3,
    // Bits 8-15 reserved for scheduler-specific flags
}
