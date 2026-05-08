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
    /// Save the current interrupt-enable state (full RFLAGS on x64, full
    /// DAIF on ARM64) and disable interrupts atomically. Pair with
    /// <see cref="RestoreIrq"/> to keep nested cli/sti regions correct.
    /// </summary>
    [LibraryImport("*", EntryPoint = "_native_cpu_save_irq_and_disable")]
    [SuppressGCTransition]
    public static partial ulong SaveIrqAndDisable();

    /// <summary>
    /// Restore an interrupt-enable state previously captured by
    /// <see cref="SaveIrqAndDisable"/>.
    /// </summary>
    [LibraryImport("*", EntryPoint = "_native_cpu_restore_irq")]
    [SuppressGCTransition]
    public static partial void RestoreIrq(ulong saved);
}
