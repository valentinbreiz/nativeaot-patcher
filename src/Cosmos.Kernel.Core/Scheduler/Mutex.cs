using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.Core.IO;
using SchedThread = Cosmos.Kernel.Core.Scheduler.Thread;

namespace Cosmos.Kernel.Core.Scheduler;

/// <summary>
/// A simple mutex primitive for protecting critical sections in the scheduler.
/// </summary>
/// <remarks>
/// This mutex supports recursive locking by the same thread.
/// </remarks>
public class Mutex : IDisposable
{
    /// <summary>
    /// Protects access to the mutex internal state.
    /// </summary>
    private SpinLock _lockGuard;

    /// <summary>
    /// The thread that currently owns the mutex, or <c>null</c> if unlocked.
    /// </summary>
    private SchedThread? _ownerThread;

    /// <summary>
    /// The recursion depth for the owning thread.
    /// </summary>
    private int _recursionDepth;

    /// <summary>
    /// Threads waiting to acquire the mutex.
    /// </summary>
    private readonly List<SchedThread> _waitingThreads;

    /// <summary>
    /// Creates a new mutex instance.
    /// </summary>
    public Mutex()
    {
        _ownerThread = null;
        _recursionDepth = 0;
        _waitingThreads = [];
    }

    /// <summary>
    /// Acquires the mutex. Blocks if already held by another thread.
    /// </summary>
    public void Acquire()
    {
        SchedThread? currentThread = SchedulerManager.GetCpuState(SchedulerManager.GetCurrentCpuId()).CurrentThread;

        if (currentThread == null)
        {
            return;
        }

        int spinAttempts = 0;
        while (true)
        {
            // Acquire spinlock to protect mutex state
            while (!_lockGuard.TryAcquire())
            {
                spinAttempts++;
                if (spinAttempts > 10000)
                {
                    InternalCpu.Halt();
                    spinAttempts = 0;
                }
            }

            // Check if mutex is free
            if (_ownerThread == null)
            {
                _ownerThread = currentThread;
                _recursionDepth = 1;
                _lockGuard.Release();
                return;
            }

            // Check if same thread (recursive lock)
            if (_ownerThread == currentThread)
            {
                _recursionDepth++;
                _lockGuard.Release();
                return;
            }

            // Mutex held by another thread - add to wait queue and block
            if (!_waitingThreads.Contains(currentThread!))
            {
                _waitingThreads.Add(currentThread!);
            }

            _lockGuard.Release();

            // Set thread state to blocked
            if (currentThread != null)
            {
                SchedulerManager.BlockThread(currentThread.CpuId, currentThread);
                InternalCpu.Halt();
            }

            spinAttempts = 0;
        }
    }

    /// <summary>
    /// Tries to acquire the mutex without blocking.
    /// </summary>
    /// <returns>true if acquired, false if held by another thread.</returns>
    public bool TryAcquire()
    {
        SchedThread? currentThread = SchedulerManager.GetCpuState(SchedulerManager.GetCurrentCpuId()).CurrentThread;

        _lockGuard.Acquire();

        bool acquired = false;

        if (_ownerThread == null)
        {
            _ownerThread = currentThread;
            _recursionDepth = 1;
            acquired = true;
        }
        else if (_ownerThread == currentThread)
        {
            _recursionDepth++;
            acquired = true;
        }

        _lockGuard.Release();

        return acquired;
    }

    /// <summary>
    /// Releases the mutex. Must be called by the thread that holds it.
    /// </summary>
    /// <remarks>
    /// Recursive locks must be released the same number of times they were acquired.
    /// </remarks>
    public void Release()
    {
        SchedThread? currentThread = SchedulerManager.GetCpuState(SchedulerManager.GetCurrentCpuId()).CurrentThread;

        _lockGuard.Acquire();

        if (_ownerThread != currentThread)
        {
            _lockGuard.Release();
            return; // Error: not the owner
        }

        _recursionDepth--;

        if (_recursionDepth == 0)
        {
            _ownerThread = null;

            // Wake up one waiting thread if any
            if (_waitingThreads.Count > 0)
            {
                SchedThread waitingThread = _waitingThreads[0];
                _waitingThreads.RemoveAt(0);
                SchedulerManager.ReadyThread(waitingThread.CpuId, waitingThread);
            }
        }

        _lockGuard.Release();
    }

    /// <summary>
    /// Gets whether this mutex is currently locked by any thread.
    /// </summary>
    public bool IsLocked
    {
        get
        {
            _lockGuard.Acquire();
            bool locked = _ownerThread != null;
            _lockGuard.Release();
            return locked;
        }
    }

    /// <summary>
    /// Gets the current owner thread.
    /// </summary>
    public SchedThread? OwnerThread
    {
        get
        {
            _lockGuard.Acquire();
            SchedThread? owner = _ownerThread;
            _lockGuard.Release();
            return owner;
        }
    }

    /// <summary>
    /// Gets the number of waiting threads.
    /// </summary>
    public int WaitingThreadCount
    {
        get
        {
            _lockGuard.Acquire();
            int count = _waitingThreads.Count;
            _lockGuard.Release();
            return count;
        }
    }

    public void Dispose()
    {
        while (_waitingThreads.Count > 0)
        {
            _lockGuard.Acquire();
            SchedThread waitingThread = _waitingThreads[0];
            _waitingThreads.RemoveAt(0);
            _lockGuard.Release();
            SchedulerManager.ReadyThread(waitingThread.CpuId, waitingThread);
        }
        _ownerThread = null;
        _recursionDepth = 0;
        GC.SuppressFinalize(this);
    }
}
