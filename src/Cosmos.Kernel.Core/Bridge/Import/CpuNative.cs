using System.Runtime.InteropServices;

namespace Cosmos.Kernel.Core.Bridge;

public static partial class CpuNative
{
    [LibraryImport("*", EntryPoint = "_native_cpu_disable_interrupts")]
    [SuppressGCTransition]
    public static partial void DisableInterrupts();

    [LibraryImport("*", EntryPoint = "_native_cpu_enable_interrupts")]
    [SuppressGCTransition]
    public static partial void EnableInterrupts();

    [LibraryImport("*", EntryPoint = "_native_cpu_halt")]
    [SuppressGCTransition]
    public static partial void Halt();

    /// <summary>
    /// Atomically captures the current interrupt-enable state and disables interrupts.
    /// Pair with <see cref="RestoreIrq"/> for nested save/restore. Reserved for the
    /// upcoming precise stack scanner (issue #346); not used by InterruptScope today
    /// because the conservative scanner false-roots when InterruptScope's layout grows.
    /// </summary>
    [LibraryImport("*", EntryPoint = "_native_cpu_save_irq_and_disable")]
    [SuppressGCTransition]
    public static partial ulong SaveIrqAndDisable();

    /// <summary>
    /// Restores a previously captured interrupt state.
    /// </summary>
    [LibraryImport("*", EntryPoint = "_native_cpu_restore_irq")]
    [SuppressGCTransition]
    public static partial void RestoreIrq(ulong flags);
}
