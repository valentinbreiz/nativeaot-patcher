using System.Diagnostics.CodeAnalysis;

namespace Cosmos.Kernel.Core.Scheduler;

/// <summary>
/// Per-CPU scheduling state.
/// </summary>
[Experimental(Experimentals.SchedulerSeamDiagId)]
public class PerCpuState : SchedulerExtensible
{
    // ===== Identity =====

    /// <summary>
    /// ID of the CPU this state belongs to.
    /// </summary>
    public uint CpuId { get; internal set; }

    // ===== Current Execution =====

    /// <summary>
    /// Thread currently executing on this CPU, or <see langword="null"/>
    /// before the scheduler has run.
    /// </summary>
    public Thread? CurrentThread { get; internal set; }

    /// <summary>
    /// This CPU's idle thread, scheduled when no other thread is runnable.
    /// </summary>
    public Thread? IdleThread { get; internal set; }

    // ===== Timing =====

    /// <summary>
    /// Timestamp of the last timer tick processed on this CPU.
    /// </summary>
    public ulong LastTickAt { get; internal set; }

    // Set by ReadyThread when it wakes a thread (typically an ISR-side
    // InterruptEvent.Signal); consumed by ReschedulePendingFromIrq on
    // hardware-IRQ exit so the woken thread runs immediately instead of
    // sitting in the run queue until the next timer tick.
    internal bool _needReschedule;

    // ===== Synchronization =====
    internal SpinLock Lock;
}
