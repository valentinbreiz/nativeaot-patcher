namespace Cosmos.Kernel.Core.Scheduler;

/// <summary>
/// Base class for objects that can hold scheduler-specific extension data.
/// </summary>
public abstract class SchedulerExtensible
{
    /// <summary>
    /// Scheduler-specific data. Each scheduler defines its own class
    /// and stores an instance here.
    /// </summary>
    public object SchedulerData { get; set; }

    /// <summary>
    /// Type-safe accessor for extension data.
    /// </summary>
    public T GetSchedulerData<T>() where T : class => (T)SchedulerData;
}
