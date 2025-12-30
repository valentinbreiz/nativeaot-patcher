namespace Cosmos.Kernel.Core.Scheduler;

/// <summary>
/// Thread Control Block for scheduling.
/// </summary>
public unsafe class Thread : SchedulerExtensible
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

    /// <summary>
    /// Default stack size for new threads (64KB).
    /// </summary>
    public const nuint DefaultStackSize = 64 * 1024;

    /// <summary>
    /// Allocates and initializes the thread stack with initial context.
    /// After this call, the thread is ready to be scheduled.
    /// </summary>
    /// <param name="entryPoint">Thread entry point function address.</param>
    /// <param name="codeSegment">Code segment selector (CS).</param>
    /// <param name="arg">Optional argument passed to entry point.</param>
    /// <param name="stackSize">Stack size in bytes.</param>
    public void InitializeStack(nuint entryPoint, ushort codeSegment, nuint arg = 0, nuint stackSize = DefaultStackSize)
    {
        // Allocate stack memory
        StackSize = stackSize;
        StackBase = (nuint)Memory.MemoryOp.Alloc((uint)stackSize);

        // Stack grows downward - start at top
        nuint stackTop = StackBase + stackSize;

        // Reserve space for ThreadContext at top of stack
        nuint contextAddr = stackTop - (nuint)ThreadContext.Size;

        // Align to 16 bytes (required for XMM operations)
        contextAddr &= ~(nuint)0xF;

        // Initialize the context
        ThreadContext* context = (ThreadContext*)contextAddr;
        context->Initialize(entryPoint, codeSegment, arg);

        // The stack pointer points to the start of the context
        // (where XMM registers are, as expected by the IRQ stub)
        StackPointer = contextAddr;
        InstructionPointer = entryPoint;
        State = ThreadState.Created;
    }

    /// <summary>
    /// Gets a pointer to the thread's saved context.
    /// Only valid when thread is not running.
    /// </summary>
    public ThreadContext* GetContext()
    {
        return (ThreadContext*)StackPointer;
    }
}
