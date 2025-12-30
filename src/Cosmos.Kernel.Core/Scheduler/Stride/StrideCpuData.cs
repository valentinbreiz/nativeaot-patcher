using System.Collections.Generic;

namespace Cosmos.Kernel.Core.Scheduler.Stride;

/// <summary>
/// Stride scheduler per-CPU extension data.
/// </summary>
public class StrideCpuData
{
    /// <summary>
    /// Sum of tickets in run queue.
    /// </summary>
    public ulong TotalTickets { get; set; }

    /// <summary>
    /// Global virtual time.
    /// </summary>
    public ulong GlobalPass { get; set; }

    /// <summary>
    /// Timestamp of last global pass update.
    /// </summary>
    public ulong LastPassUpdate { get; set; }

    /// <summary>
    /// Run queue sorted by Pass value (ascending).
    /// </summary>
    public List<Thread> RunQueue { get; } = new();
}
