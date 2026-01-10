// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Cosmos.Kernel.Core.CPU;

/// <summary>
/// Low-level CPU operations that can be used by Core components like the heap.
/// </summary>
public static partial class InternalCpu
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
    /// Creates a scope that disables interrupts and automatically re-enables them on dispose.
    /// Usage: using (InternalCpu.DisableInterruptsScope()) { ... }
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static InterruptScope DisableInterruptsScope()
    {
        return new InterruptScope();
    }

    /// <summary>
    /// A disposable scope that disables interrupts on creation and re-enables them on dispose.
    /// </summary>
    public ref struct InterruptScope
    {
        private bool _disposed;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public InterruptScope()
        {
            _disposed = false;
            InternalCpu.DisableInterrupts();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                InternalCpu.EnableInterrupts();
            }
        }
    }
}
