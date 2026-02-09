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
    public nuint StackPointer { get; internal set; }
    public nuint InstructionPointer { get; internal set; }
    public nuint StackBase { get; internal set; }
    public nuint StackSize { get; internal set; }

    // ===== Generic Timing =====
    public ulong CreatedAt { get; set; }
    public ulong TotalRuntime { get; set; }
    public ulong LastScheduledAt { get; set; }
    public ulong WakeupTime { get; set; }
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    private object[][] _threadStaticStorage;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.


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

        // Stack layout (growing downward from top):
        // [StackBase + stackSize] = Top of usable stack
        // ... usable stack space for function calls ...
        // [contextAddr + ThreadContext.Size] = End of context
        // [contextAddr] = Start of ThreadContext (where StackPointer points)
        // [StackBase] = Bottom of stack

        nuint stackTop = StackBase + stackSize;

        // Place ThreadContext at the BOTTOM of the stack
        // The usable stack space is above it
        nuint contextAddr = StackBase;

        // Align context to 16 bytes (required for XMM operations)
        contextAddr = (contextAddr + 0xF) & ~(nuint)0xF;

        // Calculate usable stack top (above the context)
        nuint usableStackTop = stackTop;

        // Initialize the context with the usable stack top
        ThreadContext* context = (ThreadContext*)contextAddr;
        context->Initialize(entryPoint, codeSegment, arg, usableStackTop);

        // The StackPointer points to the start of the context
        // (where XMM registers are, as expected by the IRQ stub)
        StackPointer = contextAddr;
        InstructionPointer = entryPoint;
        State = ThreadState.Created;
    }

    public ref object[][] GetThreadStaticStorage()
    {
        return ref _threadStaticStorage;
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
