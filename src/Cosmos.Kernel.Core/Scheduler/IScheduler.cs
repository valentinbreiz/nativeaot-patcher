using System.Diagnostics.CodeAnalysis;

namespace Cosmos.Kernel.Core.Scheduler;

/// <summary>
/// A scheduling policy: everything that decides which thread runs when.
/// The mechanism around it (the thread registry, the lifecycle transitions,
/// the timer-tick entry, the register save and restore) is fixed.
/// Install an implementation with <see cref="SchedulerManager.SetScheduler"/>.
/// <para>
/// Every hook runs in the kernel's most sensitive window. Most are called
/// with interrupts already masked, and the two scheduling hooks run inside
/// the timer interrupt itself; each member below says which. No hook may
/// block, park, or wait, and none may allocate on the tick path. The hooks
/// the manager does not mask (<see cref="InitializeCpu"/>,
/// <see cref="ShutdownCpu"/>, <see cref="SetPriority"/>, and the two
/// run-queue diagnostics) must take
/// <see cref="SchedulerManager.MaskInterrupts"/> themselves before touching
/// the run structure.
/// </para>
/// <para>
/// Several hooks have no mechanism-side caller yet, because the kernel runs
/// on one CPU and nothing in-tree sets priorities. Implement them, but do
/// not expect them to be exercised.
/// </para>
/// <para>Inspired by Ekiben's EkibenScheduler trait.</para>
/// </summary>
[Experimental(Experimentals.SchedulerSeamDiagId)]
public interface IScheduler
{
    // ========== Identity ==========

    /// <summary>
    /// Display name for this policy, used in boot logs and reported on the
    /// ring as <c>SchedulerInfo.SchedulerName</c>.
    /// </summary>
    string Name { get; }

    // ========== Lifecycle ==========

    /// <summary>
    /// Allocate this policy's per-CPU bookkeeping into
    /// <see cref="SchedulerExtensible.SchedulerData"/> on
    /// <paramref name="cpuState"/>. Called once per CPU by
    /// <see cref="SchedulerManager.SetScheduler"/>, in thread context under a
    /// spinlock, with interrupts enabled.
    /// </summary>
    /// <param name="cpuState">CPU whose state to attach bookkeeping to.</param>
    void InitializeCpu(PerCpuState cpuState);

    /// <summary>
    /// Release the per-CPU bookkeeping, leaving a clean slot for the incoming
    /// policy. Called once per CPU by
    /// <see cref="SchedulerManager.SetScheduler"/> before the replacement is
    /// initialized, in thread context under a spinlock, with interrupts
    /// enabled. Thread slots are not cleared here; see
    /// <see cref="SchedulerExtensible.SchedulerData"/>.
    /// </summary>
    /// <param name="cpuState">CPU whose state to release bookkeeping from.</param>
    void ShutdownCpu(PerCpuState cpuState);

    // ========== Thread Lifecycle ==========

    /// <summary>
    /// Allocate this policy's per-thread bookkeeping into
    /// <see cref="SchedulerExtensible.SchedulerData"/> on
    /// <paramref name="thread"/>. Do not queue the thread yet:
    /// <see cref="OnThreadReady"/> does that. Called with interrupts masked.
    /// </summary>
    /// <param name="cpuState">CPU the thread is being created on.</param>
    /// <param name="thread">Thread entering this policy's management.</param>
    void OnThreadCreate(PerCpuState cpuState, Thread thread);

    /// <summary>
    /// Make the thread runnable: place it and insert it into the run
    /// structure. Called for a first start, for a wake, and for a sleep
    /// expiry, so it can arrive both from thread context with interrupts
    /// masked and from inside the timer interrupt.
    /// </summary>
    /// <param name="cpuState">CPU the thread is queued on.</param>
    /// <param name="thread">Thread becoming runnable.</param>
    void OnThreadReady(PerCpuState cpuState, Thread thread);

    /// <summary>
    /// Remove the thread from the run structure and save whatever must
    /// survive the park. Called for a block and for a timed sleep, with
    /// interrupts masked.
    /// </summary>
    /// <param name="cpuState">CPU the thread was queued on.</param>
    /// <param name="thread">Thread parking.</param>
    void OnThreadBlocked(PerCpuState cpuState, Thread thread);

    /// <summary>
    /// Remove the thread everywhere and drop its bookkeeping. Called with
    /// interrupts masked, before the mechanism unregisters the thread.
    /// </summary>
    /// <param name="cpuState">CPU the thread was managed on.</param>
    /// <param name="thread">Thread terminating.</param>
    void OnThreadExit(PerCpuState cpuState, Thread thread);

    /// <summary>
    /// Re-insert a thread that gave up the CPU while still runnable. Called
    /// from an explicit yield in thread context with interrupts masked, and
    /// from the preemption path in interrupt context, where it runs
    /// <em>after</em> <see cref="PickNext"/> has already chosen the
    /// replacement: the outgoing thread is not a candidate for the switch it
    /// is being preempted by.
    /// </summary>
    /// <param name="cpuState">CPU the thread is queued on.</param>
    /// <param name="thread">Thread giving up the CPU.</param>
    void OnThreadYield(PerCpuState cpuState, Thread thread);

    // ========== Scheduling Decisions ==========

    /// <summary>
    /// Pick the next thread to run. Called in interrupt context, on every
    /// reschedule. The outgoing thread is not in the run structure at this
    /// point; it goes back through <see cref="OnThreadYield"/> afterwards.
    /// </summary>
    /// <param name="cpuState">CPU to schedule.</param>
    /// <returns>
    /// The thread to switch to, or <see langword="null"/> to fall back to the
    /// CPU's idle thread. Returning a dead or parked thread is not checked.
    /// </returns>
    Thread? PickNext(PerCpuState cpuState);

    /// <summary>
    /// Put back a thread the mechanism picked but could not switch to.
    /// Declared for completeness; nothing calls it today.
    /// </summary>
    /// <param name="cpuState">CPU the pick was made for.</param>
    /// <param name="thread">Thread that could not be switched to.</param>
    void OnPickFailed(PerCpuState cpuState, Thread thread);

    /// <summary>
    /// Account the elapsed time and decide whether to preempt. Called from
    /// the timer interrupt, on every tick, and it is the only hook that runs
    /// on a fixed schedule: keep it allocation-free.
    /// </summary>
    /// <param name="cpuState">CPU being ticked.</param>
    /// <param name="current">Thread running on it.</param>
    /// <param name="elapsedNs">
    /// The configured tick interval, not a measurement. It is
    /// <see cref="SchedulerManager.DefaultQuantumNs"/>, which is what the
    /// boot path arms the scheduler timer at.
    /// </param>
    /// <returns>
    /// <see langword="true"/> to request a reschedule, which runs
    /// <see cref="PickNext"/> before this interrupt returns.
    /// </returns>
    bool OnTick(PerCpuState cpuState, Thread current, ulong elapsedNs);

    // ========== Load Balancing ==========

    /// <summary>
    /// Choose a starting CPU for a new or migrating thread, honouring
    /// <see cref="ThreadFlags.Pinned"/>. Dormant until SMP lands: nothing
    /// calls it today, and this hook receives no <see cref="PerCpuState"/>.
    /// </summary>
    /// <param name="thread">Thread being placed.</param>
    /// <param name="currentCpu">CPU the thread is on now.</param>
    /// <param name="cpuCount">Number of CPUs the scheduler manages.</param>
    /// <returns>The CPU to place the thread on.</returns>
    uint SelectCpu(Thread thread, uint currentCpu, uint cpuCount);

    /// <summary>
    /// Move a thread's bookkeeping, and any virtual-time base, between CPUs.
    /// The mechanism never calls this; a policy calls it from its own
    /// <see cref="Balance"/>.
    /// </summary>
    /// <param name="thread">Thread being migrated.</param>
    /// <param name="fromState">CPU the thread is leaving.</param>
    /// <param name="toState">CPU the thread is joining.</param>
    void OnThreadMigrate(Thread thread, PerCpuState fromState, PerCpuState toState);

    /// <summary>
    /// Rebalance load across CPUs, honouring <see cref="ThreadFlags.Pinned"/>.
    /// Dormant until SMP lands: nothing calls it today.
    /// </summary>
    /// <param name="cpuState">CPU asking for work, or offering it.</param>
    /// <param name="allCpuStates">Every CPU the scheduler manages.</param>
    void Balance(PerCpuState cpuState, PerCpuState[] allCpuStates);

    // ========== Dynamic Reconfiguration ==========

    /// <summary>
    /// Change a thread's priority. The meaning is policy-defined: Stride
    /// reads it as a ticket count, a real-time policy would read it as a
    /// priority level. Reached only through
    /// <see cref="SchedulerManager.SetPriority"/>, which holds the per-CPU
    /// spinlock but does <em>not</em> mask interrupts, so a hook that touches
    /// the run structure must take
    /// <see cref="SchedulerManager.MaskInterrupts"/> itself.
    /// </summary>
    /// <param name="cpuState">CPU whose state guards the update.</param>
    /// <param name="thread">Thread to reprioritize.</param>
    /// <param name="priority">New policy-defined priority.</param>
    void SetPriority(PerCpuState cpuState, Thread thread, long priority);

    /// <summary>
    /// Report a thread's current priority in the same policy-defined units
    /// <see cref="SetPriority"/> takes. Called from thread context with no
    /// guard at all, including by the <c>SchedulerInfo</c> facade when it
    /// snapshots a thread, so it must not mutate anything.
    /// </summary>
    /// <param name="thread">Thread to query. This hook receives no <see cref="PerCpuState"/>.</param>
    /// <returns>The thread's priority, or 0 when the policy does not track one for it.</returns>
    long GetPriority(Thread thread);

    // ========== Diagnostics ==========

    /// <summary>
    /// Report how many threads are waiting in the run structure. Read-only
    /// introspection for the <c>SchedulerInfo</c> facade, called from thread
    /// context with no guard, so guard it yourself with
    /// <see cref="SchedulerManager.MaskInterrupts"/> if a concurrent tick
    /// could be mutating the structure underneath it.
    /// </summary>
    /// <param name="cpuState">CPU to inspect.</param>
    /// <returns>The number of queued threads.</returns>
    int GetRunQueueCount(PerCpuState cpuState);

    /// <summary>
    /// Report the queued thread at a position. Same calling context and same
    /// guarding duty as <see cref="GetRunQueueCount"/>.
    /// </summary>
    /// <param name="cpuState">CPU to inspect.</param>
    /// <param name="index">Queue position, from 0 to <see cref="GetRunQueueCount"/> exclusive.</param>
    /// <returns>The queued thread, or <see langword="null"/> when the index is out of range.</returns>
    Thread? GetRunQueueThread(PerCpuState cpuState, int index);
}
