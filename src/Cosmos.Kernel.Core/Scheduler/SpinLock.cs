namespace Cosmos.Kernel.Core.Scheduler;

/// <summary>
/// Simple spinlock for SMP synchronization.
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

    public bool IsLocked => _locked != 0;
}
