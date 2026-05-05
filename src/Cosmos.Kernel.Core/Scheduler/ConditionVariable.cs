using System.Diagnostics.CodeAnalysis;
using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.Core.IO;
using SchedThread = Cosmos.Kernel.Core.Scheduler.Thread;

namespace Cosmos.Kernel.Core.Scheduler;

/// <summary>
/// A condition variable primitive for coordinating scheduler threads around state changes.
/// </summary>
public class ConditionVariable : IDisposable
{
    /// <summary>
    /// Protects access to the condition variable internal state.
    /// </summary>
    private SpinLock _lockGuard;

    /// <summary>
    /// Threads currently waiting for the condition.
    /// </summary>
    private readonly List<SchedThread> _waitingThreads;

    /// <summary>
    /// Gets the number of waiting threads.
    /// </summary>
    public int WaitingThreadCount => _waitingThreads.Count;

    /// <summary>
    /// Creates a new condition variable instance.
    /// </summary>
    public ConditionVariable()
    {
        _waitingThreads = [];
    }

    /// <summary>
    /// Waits for a condition to be signaled. The associated mutex must be held by the calling thread.
    /// </summary>
    /// <param name="mutex">The mutex protecting the condition.</param>
    /// <remarks>
    /// This method releases the mutex while the thread is blocked and re-acquires it before returning.
    /// </remarks>
    public void Wait(Mutex mutex)
    {
        SchedThread? currentThread;

        do
        {
            currentThread = SchedulerManager.GetCpuState(SchedulerManager.GetCurrentCpuId()).CurrentThread;
        }
        while (currentThread == null);

        _lockGuard.Acquire();
        if (!_waitingThreads.Contains(currentThread!))
        {
            _waitingThreads.Add(currentThread!);
        }
        _lockGuard.Release();

        // Release the mutex while waiting
        mutex.Release();

        SchedulerManager.BlockThread(currentThread.CpuId, currentThread);
        InternalCpu.Halt();

        // Reacquire the mutex before returning
        mutex.Acquire();
    }

    /// <summary>
    /// Waits for a condition with a timeout.
    /// </summary>
    /// <param name="mutex">The mutex protecting the condition.</param>
    /// <param name="timeoutMs">Timeout in milliseconds.</param>
    /// <returns>true if signaled, false if timeout occurred.</returns>
    public bool WaitTimeout(Mutex mutex, uint timeoutMs)
    {
        SchedThread? currentThread = SchedulerManager.GetCpuState(SchedulerManager.GetCurrentCpuId()).CurrentThread;
        if (currentThread == null)
        {
            return false;
        }

        // Release the mutex while waiting
        mutex.Release();

        _lockGuard.Acquire();
        if (!_waitingThreads.Contains(currentThread!))
        {
            _waitingThreads.Add(currentThread!);
        }
        _lockGuard.Release();

        // Put thread to sleep with timeout
        SchedulerManager.Sleep(currentThread.CpuId, currentThread, timeoutMs);

        // Reacquire the mutex before returning
        mutex.Acquire();

        if (currentThread.WakeupTime > 0)
        {
            currentThread.WakeupTime = 0;
            return true;
        }
        else
        {
            return false;
        }
    }

    /// <summary>
    /// Signals one waiting thread, if there isn't any waiting thread then do nothing.
    /// </summary>
    public void Signal()
    {
        _lockGuard.Acquire();

        if (_waitingThreads.Count > 0)
        {
            SchedThread waitingThread = _waitingThreads[0];
            _waitingThreads.RemoveAt(0);

            // Wake the thread by marking it ready
            SchedulerManager.ReadyThread(waitingThread.CpuId, waitingThread);
        }

        _lockGuard.Release();
    }

    /// <summary>
    /// Signals all waiting threads, if there isn't any waiting thread then do nothing.
    /// </summary>
    public void SignalAll()
    {
        _lockGuard.Acquire();

        foreach (SchedThread thread in _waitingThreads)
        {
            // Wake each thread by marking it ready
            SchedulerManager.ReadyThread(thread.CpuId, thread);
        }

        _waitingThreads.Clear();

        _lockGuard.Release();
    }

    public void Dispose()
    {
        SignalAll();
        GC.SuppressFinalize(this);
    }
}
