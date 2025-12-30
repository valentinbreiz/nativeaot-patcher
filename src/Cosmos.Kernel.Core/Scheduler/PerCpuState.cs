namespace Cosmos.Kernel.Core.Scheduler;

/// <summary>
/// Per-CPU scheduling state.
/// </summary>
public class PerCpuState : SchedulerExtensible
{
    // ===== Identity =====
    public uint CpuId { get; set; }

    // ===== Current Execution =====
    public Thread CurrentThread { get; set; }
    public Thread IdleThread { get; set; }

    // ===== Timing =====
    public ulong LastTickAt { get; set; }

    // ===== Synchronization =====
    public SpinLock Lock;
}
