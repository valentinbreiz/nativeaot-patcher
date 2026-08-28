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
    /// </summary>
    public object? SchedulerData { get; set; }

    /// <summary>
    /// Reads <see cref="SchedulerData"/> as <typeparamref name="T"/>. This is
    /// a cast, not a probe: a slot holding another policy's record throws
    /// rather than returning null, and on the tick path that throw lands
    /// inside the timer interrupt. Use it only where the policy owns every
    /// object it can be handed, which the built-in policy does because it is
    /// installed before the first thread exists. A policy installed over a
    /// running kernel inherits threads carrying the previous policy's
    /// records, so it reads the slot with <c>as</c> instead.
    /// </summary>
    /// <typeparam name="T">The record type this policy stores in the slot.</typeparam>
    /// <returns>
    /// The stored record, or <see langword="null"/> when the slot is empty.
    /// A thread's slot is empty before <see cref="IScheduler.OnThreadCreate"/>
    /// and again after <see cref="IScheduler.OnThreadExit"/>, so a hook can
    /// observe null for a thread that exited mid-tick.
    /// </returns>
    /// <exception cref="InvalidCastException">
    /// The slot holds a record of another type.
    /// </exception>
    public T? GetSchedulerData<T>() where T : class => (T?)SchedulerData;
}
