namespace Cosmos.Kernel.Core.Scheduler;

/// <summary>
/// Interface for pluggable scheduling algorithms.
/// Inspired by Ekiben's EkibenScheduler trait.
/// </summary>
public interface IScheduler
{
    // ========== Identity ==========

    /// <summary>
    /// Unique name for this scheduler.
    /// </summary>
    string Name { get; }

    // ========== Lifecycle ==========

    /// <summary>
    /// Initialize scheduler for a specific CPU.
    /// Create and assign PerCpuState.SchedulerData here.
    /// </summary>
    void InitializeCpu(PerCpuState cpuState);

    /// <summary>
    /// Cleanup when scheduler is being replaced or shutdown.
    /// </summary>
    void ShutdownCpu(PerCpuState cpuState);

    // ========== Thread Lifecycle ==========

    /// <summary>
    /// A new thread is being added to this CPU's management.
    /// Create and assign Thread.SchedulerData here.
    /// </summary>
    void OnThreadCreate(PerCpuState cpuState, Thread thread);

    /// <summary>
    /// Thread is ready to run (first time or after wakeup).
    /// Add to run queue.
    /// </summary>
    void OnThreadReady(PerCpuState cpuState, Thread thread);

    /// <summary>
    /// Thread has blocked (I/O, lock, sleep, etc.).
    /// Remove from run queue, save state for resume.
    /// </summary>
    void OnThreadBlocked(PerCpuState cpuState, Thread thread);

    /// <summary>
    /// Thread is terminating.
    /// Cleanup Thread.SchedulerData.
    /// </summary>
    void OnThreadExit(PerCpuState cpuState, Thread thread);

    /// <summary>
    /// Thread voluntarily yields remaining time slice.
    /// </summary>
    void OnThreadYield(PerCpuState cpuState, Thread thread);

    // ========== Scheduling Decisions ==========

    /// <summary>
    /// Pick the next thread to run.
    /// Returns null if no runnable threads (run idle).
    /// </summary>
    Thread PickNext(PerCpuState cpuState);

    /// <summary>
    /// Called when picked thread couldn't be scheduled.
    /// </summary>
    void OnPickFailed(PerCpuState cpuState, Thread thread);

    /// <summary>
    /// Timer tick - update accounting, check for preemption.
    /// </summary>
    /// <returns>True if reschedule needed (preemption)</returns>
    bool OnTick(PerCpuState cpuState, Thread current, ulong elapsedNs);

    // ========== Load Balancing ==========

    /// <summary>
    /// Select best CPU for a new or migrating thread.
    /// </summary>
    uint SelectCpu(Thread thread, uint currentCpu, uint cpuCount);

    /// <summary>
    /// Thread is migrating between CPUs.
    /// </summary>
    void OnThreadMigrate(Thread thread, PerCpuState fromState, PerCpuState toState);

    /// <summary>
    /// Periodic load balancing opportunity.
    /// </summary>
    void Balance(PerCpuState cpuState, PerCpuState[] allCpuStates);

    // ========== Dynamic Reconfiguration ==========

    /// <summary>
    /// Thread's priority/weight is changing.
    /// Interpretation is scheduler-specific.
    /// </summary>
    void SetPriority(PerCpuState cpuState, Thread thread, long priority);

    /// <summary>
    /// Get thread's current priority/weight.
    /// </summary>
    long GetPriority(Thread thread);
}
