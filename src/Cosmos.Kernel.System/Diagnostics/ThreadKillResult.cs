namespace Cosmos.Kernel.System.Diagnostics;

/// <summary>
/// Outcome of <see cref="SchedulerInfo.RequestKill"/>.
/// </summary>
public enum ThreadKillResult : byte
{
    /// <summary>No live thread has the requested ID.</summary>
    NotFound,

    /// <summary>The thread was removed from its run queue and terminated.</summary>
    Killed,

    /// <summary>
    /// The thread is currently running; it was marked dead and will be
    /// reaped by the scheduler on its next reschedule.
    /// </summary>
    MarkedForExit,

    /// <summary>The request named an idle thread, which cannot be killed.</summary>
    RefusedIdle,

    /// <summary>
    /// The thread is blocked or sleeping. Waiting threads sit outside the
    /// run queues with their share already returned to the policy, so
    /// terminating one from here would return it twice; wake it first.
    /// </summary>
    RefusedBlocked,
}
