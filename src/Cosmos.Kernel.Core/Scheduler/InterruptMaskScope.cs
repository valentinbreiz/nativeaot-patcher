using System.Diagnostics.CodeAnalysis;
using Cosmos.Kernel.Core.CPU;

namespace Cosmos.Kernel.Core.Scheduler;

/// <summary>
/// Masks maskable interrupts on the current CPU for the lifetime of the
/// scope and restores the previous interrupt state on dispose. Obtained
/// from <see cref="SchedulerManager.MaskInterrupts"/>; use it in a
/// <see langword="using"/> statement around scheduler entry points the
/// manager does not already guard (diagnostics reads, tuning setters), so
/// the timer tick cannot observe a half-mutated run structure.
/// </summary>
[Experimental(Experimentals.SchedulerSeamDiagId)]
public ref struct InterruptMaskScope
{
    private InternalCpu.InterruptScope _scope;

    internal InterruptMaskScope(InternalCpu.InterruptScope scope)
    {
        _scope = scope;
    }

    /// <summary>
    /// Restores the interrupt state captured when the scope was taken.
    /// </summary>
    public void Dispose() => _scope.Dispose();
}
