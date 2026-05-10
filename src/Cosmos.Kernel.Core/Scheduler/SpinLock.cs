using Cosmos.Kernel.Core.Bridge;

namespace Cosmos.Kernel.Core.Scheduler;

/// <summary>
/// Simple spinlock for SMP synchronization.
///
/// <para>The plain <see cref="Acquire"/> / <see cref="Release"/> pair is
/// only safe for locks that are never taken from interrupt context. If an
/// ISR can acquire the same lock as mainline code (e.g. a wait/signal
/// pair where the ISR signals), use <see cref="AcquireIrqSave"/> /
/// <see cref="ReleaseIrqRestore"/>: a single CPU holding the plain lock
/// when the ISR fires will spin forever waiting for itself.</para>
/// </summary>
public struct SpinLock
{
    private int _locked;

    public void Acquire()
    {
        while (Interlocked.CompareExchange(ref _locked, 1, 0) != 0)
        {
            // Spin until lock is acquired
        }
    }

    public void Release()
    {
        Interlocked.Exchange(ref _locked, 0);
    }

    public bool TryAcquire()
    {
        return Interlocked.CompareExchange(ref _locked, 1, 0) == 0;
    }

    /// <summary>
    /// Disable interrupts and acquire the lock atomically. Returns the
    /// prior interrupt state (full RFLAGS on x64, full DAIF on ARM64) which
    /// must be passed back to <see cref="ReleaseIrqRestore"/>. Use this
    /// whenever the same lock can be taken from both mainline and ISR
    /// context — without it, a single CPU holding the lock when its ISR
    /// fires will deadlock against itself.
    /// </summary>
    public ulong AcquireIrqSave()
    {
        ulong saved = CpuNative.SaveIrqAndDisable();
        while (Interlocked.CompareExchange(ref _locked, 1, 0) != 0)
        {
            // Spin until lock is acquired (interrupts already disabled)
        }
        return saved;
    }

    /// <summary>
    /// Release the lock and restore the prior interrupt state captured by
    /// <see cref="AcquireIrqSave"/>.
    /// </summary>
    public void ReleaseIrqRestore(ulong saved)
    {
        Interlocked.Exchange(ref _locked, 0);
        CpuNative.RestoreIrq(saved);
    }

    public readonly bool IsLocked => _locked != 0;
}
