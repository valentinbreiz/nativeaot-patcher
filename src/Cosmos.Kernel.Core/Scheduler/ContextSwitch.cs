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

    /// <summary>
    /// Sets whether the target thread is NEW (1) or RESUMED (0).
    /// NEW threads need RSP loaded from context, RESUMED threads use iretq.
    /// </summary>
    [LibraryImport("*", EntryPoint = "_native_x64_set_context_switch_new_thread")]
    [SuppressGCTransition]
    public static partial void SetContextSwitchNewThread(int isNew);
#elif ARCH_ARM64
    /// <summary>
    /// Sets the target SP for context switch. The IRQ stub will switch
    /// to this stack after the managed handler returns.
    /// </summary>
    /// <param name="newSp">New SP pointing to a saved context.</param>
    [LibraryImport("*", EntryPoint = "_native_arm64_set_context_switch_sp")]
    [SuppressGCTransition]
    public static partial void SetContextSwitchRsp(nuint newSp);

    /// <summary>
    /// Gets the current context switch target SP (for debugging).
    /// </summary>
    [LibraryImport("*", EntryPoint = "_native_arm64_get_context_switch_sp")]
    [SuppressGCTransition]
    public static partial nuint GetContextSwitchRsp();

    /// <summary>
    /// Gets the current SP value.
    /// </summary>
    [LibraryImport("*", EntryPoint = "_native_arm64_get_sp")]
    [SuppressGCTransition]
    public static partial nuint GetRsp();

    /// <summary>
    /// Sets whether the target thread is NEW (1) or RESUMED (0).
    /// NEW threads need SP loaded from context and branch to entry point.
    /// RESUMED threads use eret to return.
    /// </summary>
    [LibraryImport("*", EntryPoint = "_native_arm64_set_context_switch_new_thread")]
    [SuppressGCTransition]
    public static partial void SetContextSwitchNewThread(int isNew);
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

        // Determine if this is a NEW thread (never run before) or RESUMED
        bool isNewThread = next.State == ThreadState.Created;

        // Load next thread's stack pointer
        next.State = ThreadState.Running;
        SetContextSwitchNewThread(isNewThread ? 1 : 0);
        SetContextSwitchRsp(next.StackPointer);
    }
}
