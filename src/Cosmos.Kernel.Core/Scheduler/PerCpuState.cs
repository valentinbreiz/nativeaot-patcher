namespace Cosmos.Kernel.Core.Scheduler;

/// <summary>
/// Per-CPU scheduling state.
/// </summary>
public class PerCpuState : SchedulerExtensible
{
    // ===== Identity =====
    public uint CpuId { get; set; }

    // ===== Current Execution =====
    public Thread CurrentThread { get; internal set; }
    public Thread IdleThread { get; internal set; }

    // ===== Timing =====
    public ulong LastTickAt { get; internal set; }

    // ===== Synchronization =====
    public SpinLock Lock;
}
