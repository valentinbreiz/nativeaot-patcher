using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Cosmos.Kernel.Core.Scheduler;

/// <summary>
/// Low-level context switch operations.
/// Platform-specific implementations in native code.
/// </summary>
public static partial class ContextSwitch
{
#if ARCH_X64
    /// <summary>
    /// Sets the target RSP for context switch. The IRQ stub will switch
    /// to this stack after the managed handler returns.
    /// </summary>
    /// <param name="newRsp">New RSP pointing to a saved context.</param>
    [LibraryImport("*", EntryPoint = "_native_x64_set_context_switch_rsp")]
    [SuppressGCTransition]
    public static partial void SetContextSwitchRsp(nuint newRsp);

    /// <summary>
    /// Gets the current context switch target RSP (for debugging).
    /// </summary>
    [LibraryImport("*", EntryPoint = "_native_x64_get_context_switch_rsp")]
    [SuppressGCTransition]
    public static partial nuint GetContextSwitchRsp();

    /// <summary>
    /// Gets the current RSP value.
    /// </summary>
    [LibraryImport("*", EntryPoint = "_native_x64_get_rsp")]
    [SuppressGCTransition]
    public static partial nuint GetRsp();
#elif ARCH_ARM64
    // ARM64 implementation will be added later
    public static void SetContextSwitchRsp(nuint newRsp) { }
    public static nuint GetContextSwitchRsp() => 0;
    public static nuint GetRsp() => 0;
#endif

    /// <summary>
    /// Requests a context switch to the specified thread.
    /// Called from timer interrupt handler when preemption is needed.
    /// </summary>
    /// <param name="currentRsp">Current stack pointer (from IRQ context).</param>
    /// <param name="current">Currently running thread (may be null for idle).</param>
    /// <param name="next">Next thread to run.</param>
    public static void Switch(nuint currentRsp, Thread? current, Thread next)
    {
        // Save current thread's stack pointer
        if (current != null)
        {
            current.StackPointer = currentRsp;
            current.State = ThreadState.Ready;
        }

        // Load next thread's stack pointer
        next.State = ThreadState.Running;
        SetContextSwitchRsp(next.StackPointer);
    }
}
