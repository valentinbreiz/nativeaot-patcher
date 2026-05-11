// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.CompilerServices;
using Cosmos.Kernel.Core.Bridge;

namespace Cosmos.Kernel.Core.CPU;

/// <summary>
/// Low-level CPU operations that can be used by Core components like the heap.
/// Native imports live in Bridge/Import/CpuNative.cs.
/// </summary>
public static class InternalCpu
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DisableInterrupts() => CpuNative.DisableInterrupts();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void EnableInterrupts() => CpuNative.EnableInterrupts();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Halt() => CpuNative.Halt();

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
    /// Disables interrupts on creation, re-enables them on dispose.
    /// Deliberately does NOT save the prior state: storing a saved-flags
    /// field on this ref struct changes the stack layout of every method
    /// that uses it (Heap, GC, Scheduler, ...), and the conservative GC
    /// scanner in <c>GarbageCollector.Mark.cs</c> then false-roots
    /// weak-handle targets from stale spills in dead callee frames.
    /// If you really need to preserve the caller's interrupt state, the
    /// only current case is the ISR/mainline lock in
    /// <see cref="Scheduler.SpinLock.AcquireIrqSave"/>, use that
    /// directly instead of this scope.
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
