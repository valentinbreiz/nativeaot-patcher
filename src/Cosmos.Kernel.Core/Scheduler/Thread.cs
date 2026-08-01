using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.Core.Memory.GarbageCollector;
using Cosmos.Kernel.Core.Memory.VAS;

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

    // ===== Address Space =====
    /// <summary>
    /// Address space this thread runs in. If null, the thread uses the kernel space.
    /// </summary>
    public AddressSpace? AddressSpace { get; set; }

    /// <summary>
    /// if set then this is a userspace thread
    /// </summary>
    public Process? Process { get; set; }

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

    // ===== GC Allocation Context (TLAB) =====
    public AllocContext AllocContext;
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    private object[][] _threadStaticStorage;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.


    /// <summary>
    /// Default stack size for new threads (256KB). Must stay above CoreLib's
    /// 128KB MinExecutionStackSize (64-bit) or
    /// RuntimeHelpers.EnsureSufficientExecutionStack — called by generated
    /// record ToString/PrintMembers among others — throws
    /// InsufficientExecutionStackException on every call (#433).
    /// </summary>
    public const nuint DefaultStackSize = 256 * 1024;

    /// <summary>
    /// Maximum number of threads tracked by the global thread registry.
    /// </summary>
    public const int MaxThreadCount = 256;

    /// <summary>
    /// Allocates and initializes the thread stack with initial context.
    /// After this call, the thread is ready to be scheduled.
    /// </summary>
    /// <param name="entryPoint">Thread entry point function address.</param>
    /// <param name="codeSegment">
    /// Code segment selector (CS). For ring-3 threads (<paramref name="ring"/>
    /// == 3) this is ignored and the user code selector (0x1B) is used; pass 0.
    /// </param>
    /// <param name="arg">Optional argument passed to entry point.</param>
    /// <param name="stackSize">Stack size in bytes.</param>
    /// <param name="ring">
    /// Privilege level: 0 = ring-0/EL1h kernel thread (default), 3 = ring-3/
    /// EL0t user thread. A ring-3 thread requires <paramref name="userSpace"/>
    /// so the stack can be mapped into the process address space at a user VA.
    /// </param>
    /// <param name="userSpace">
    /// Address space the user stack is mapped into. Required for
    /// <paramref name="ring"/> == 3 (the kernel stacks ring-0 threads); ignored
    /// for ring-0. When set, the stack pages are allocated page-aligned from
    /// <see cref="PageAllocator"/> (instead of the GC heap) and mapped
    /// <c>Read|Write|User</c> at a freshly-allocated user VA, and the
    /// <see cref="ThreadContext"/>'s RSP/SP is rewritten to that user VA top.
    /// </param>
    /// <param name="userProcess">
    /// Owning <see cref="Process"/>: supplies the per-process user-VA cursor
    /// (<see cref="Process.AllocateUserStack"/>) through which a unique user
    /// stack VA is carved. Required for <paramref name="ring"/> == 3 when
    /// <paramref name="userSpace"/> is also set; ignored otherwise.
    /// </param>
    public void InitializeStack(nuint entryPoint, ushort codeSegment, nuint arg = 0, nuint stackSize = DefaultStackSize, byte ring = 0, AddressSpace? userSpace = null, Process? userProcess = null)
    {
        bool isUser = ring == 3 && userSpace != null && userProcess != null;

        // Allocate stack memory. User stacks must come from the page allocator
        // (page-aligned, identity-trackable) so they can be mapped into the
        // process address space; kernel stacks stay on the GC heap to preserve
        // the existing heap bookkeeping the GC relies on.
        nuint stackBase;
        if (isUser)
        {
            ulong pages = (stackSize + PageAllocator.PageSize - 1) / PageAllocator.PageSize;
            stackBase = (nuint)PageAllocator.AllocPages(PageType.Unmanaged, pages, zero: true);
            StackSize = (nuint)(pages * PageAllocator.PageSize);
        }
        else
        {
            StackSize = stackSize;
            stackBase = (nuint)MemoryOp.Alloc((uint)stackSize);
        }
        StackBase = stackBase;

        // Stack layout (growing downward from top):
        // [StackBase + stackSize] = Top of usable stack
        // ... usable stack space for function calls ...
        // [contextAddr + ThreadContext.Size] = End of context
        // [contextAddr] = Start of ThreadContext (where StackPointer points)
        // [StackBase] = Bottom of stack

        nuint stackTop = stackBase + StackSize;

        // Place ThreadContext at the BOTTOM of the stack
        // The usable stack space is above it
        nuint contextAddr = stackBase;

        // Align context to 16 bytes (required for XMM operations)
        contextAddr = (contextAddr + 0xF) & ~(nuint)0xF;

        // Calculate usable stack top (above the context)
        nuint usableStackTop = stackTop;

        // Initialize the context with the usable stack top
        ThreadContext* context = (ThreadContext*)contextAddr;
        context->Initialize(entryPoint, codeSegment, arg, usableStackTop, ring);

        // For ring-3 threads, the context's RSP/SP currently points into the
        // kernel heap (StackBase + StackSize) - unreachable from ring 3. Map
        // the freshly-allocated stack pages into the process address space at
        // a user VA and rewrite the context's RSP/SP to that user-VA top so
        // the iretq/eret drops CPL with a valid user stack.
        if (isUser)
        {
            ulong phys = PageAllocator.VirtualToPhysical((ulong)stackBase);
            ulong pages = StackSize / PageAllocator.PageSize;
            nuint userStackTop = userProcess!.AllocateUserStack(StackSize);
            userSpace!.Map(userStackTop - StackSize, phys, pages,
                PageFlags.Read | PageFlags.Write | PageFlags.User);
#if ARCH_X64
            // Match the alignment ThreadContext.Initialize applies
            // (16-byte aligned, then 8 off for the call ABI).
            context->Rsp = (userStackTop & ~(nuint)0xF) - 8;
#elif ARCH_ARM64
            context->Sp = userStackTop & ~(nuint)0xF;
#endif
        }

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
