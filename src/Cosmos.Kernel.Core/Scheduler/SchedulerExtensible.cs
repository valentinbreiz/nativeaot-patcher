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
    /// </summary>
    public object? SchedulerData { get; set; }

    /// <summary>
    /// Type-safe accessor for extension data.
    /// </summary>
    public T? GetSchedulerData<T>() where T : class => (T?)SchedulerData;
}
