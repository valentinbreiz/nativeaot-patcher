using System.Diagnostics.CodeAnalysis;

namespace Cosmos.Kernel.Core.Scheduler;

/// <summary>
/// Base class for objects that can hold scheduler-specific extension data.
/// </summary>
[Experimental(Experimentals.SchedulerSeamDiagId)]
public abstract class SchedulerExtensible
{
    /// <summary>
    /// Scheduler-specific data. Each scheduler defines its own class and
    /// stores an instance here from its <see cref="IScheduler"/> lifecycle
    /// hooks (<see cref="IScheduler.InitializeCpu"/>,
    /// <see cref="IScheduler.OnThreadCreate"/>), and clears it again in
    /// <see cref="IScheduler.ShutdownCpu"/> and
    /// <see cref="IScheduler.OnThreadExit"/>.
    /// <para>
    /// One slot is the whole budget: a policy needing several values defines
    /// one class holding them. And installing a policy does not empty the
    /// slots it will be handed. <see cref="SchedulerManager.SetScheduler"/>
    /// runs <see cref="IScheduler.ShutdownCpu"/> on every CPU, which is what
    /// clears the per-CPU slots, but it does not walk the thread registry, so
    /// every thread already alive reaches the incoming policy still carrying
    /// the outgoing policy's record.
    /// </para>
    /// <para>
    /// Read it with <c>as</c>, never a cast, for exactly that reason: a hook
    /// that casts throws on the first foreign record it is handed, and the
    /// tick hooks are handed theirs inside the timer interrupt. Every hook
    /// already has to handle an empty slot, so <c>as</c> costs nothing.
    /// </para>
    /// </summary>
    public object? SchedulerData { get; set; }
}
