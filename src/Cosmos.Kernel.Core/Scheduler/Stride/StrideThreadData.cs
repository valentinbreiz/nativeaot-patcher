namespace Cosmos.Kernel.Core.Scheduler.Stride;

/// <summary>
/// Stride scheduler per-thread extension data.
/// </summary>
public class StrideThreadData
{
    /// <summary>
    /// Resource weight (higher = more CPU time).
    /// </summary>
    public ulong Tickets { get; set; }

    /// <summary>
    /// Stride value = Stride1 / Tickets.
    /// </summary>
    public ulong Stride { get; set; }

    /// <summary>
    /// Virtual time position.
    /// </summary>
    public long Pass { get; set; }

    /// <summary>
    /// Remaining stride saved when blocking.
    /// </summary>
    public long Remain { get; set; }

    /// <summary>
    /// Timestamp of last wakeup (for interactive detection).
    /// </summary>
    public ulong LastWakeup { get; set; }

    /// <summary>
    /// Number of times thread has slept (I/O pattern tracking).
    /// </summary>
    public uint SleepCount { get; set; }

    /// <summary>
    /// Whether thread is detected as interactive.
    /// </summary>
    public bool IsInteractive { get; set; }

    /// <summary>
    /// Whether thread currently has priority boost.
    /// </summary>
    public bool IsBoosted { get; set; }
}
