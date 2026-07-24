using Cosmos.Kernel.Core.Memory.VAS;

namespace Cosmos.Kernel.Core.Scheduler;

/// <summary>
/// Per-CPU scheduling state.
/// </summary>
public class PerCpuState : SchedulerExtensible
{
    // ===== Identity =====
    public uint CpuId { get; set; }

    // ===== Current Execution =====
    public Thread? CurrentThread { get; internal set; }
    public Thread? IdleThread { get; internal set; }

    /// <summary>
    /// Address space currently active on this CPU. Updated during context switch.
    /// </summary>
    public AddressSpace? CurrentAddressSpace { get; set; }

    // ===== Timing =====
    public ulong LastTickAt { get; internal set; }

    // Set by ReadyThread when it wakes a thread (typically an ISR-side
    // InterruptEvent.Signal); consumed by ReschedulePendingFromIrq on
    // hardware-IRQ exit so the woken thread runs immediately instead of
    // sitting in the run queue until the next timer tick.
    internal bool NeedReschedule;

    // ===== Synchronization =====
    public SpinLock Lock;
}
