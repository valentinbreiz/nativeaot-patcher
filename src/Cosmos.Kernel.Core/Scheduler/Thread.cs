namespace Cosmos.Kernel.Core.Scheduler;

/// <summary>
/// Thread Control Block for scheduling.
/// </summary>
public class Thread : SchedulerExtensible
{
    // ===== Identity =====
    public uint Id { get; set; }
    public uint CpuId { get; set; }

    // ===== State =====
    public ThreadState State { get; set; }
    public ThreadFlags Flags { get; set; }

    // ===== Context (architecture-specific values) =====
    public nuint StackPointer { get; set; }
    public nuint InstructionPointer { get; set; }
    public nuint StackBase { get; set; }
    public nuint StackSize { get; set; }

    // ===== Generic Timing =====
    public ulong CreatedAt { get; set; }
    public ulong TotalRuntime { get; set; }
    public ulong LastScheduledAt { get; set; }
    public ulong WakeupTime { get; set; }
}
