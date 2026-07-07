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
    /// Without a scheduler thread context there is a single execution
    /// context and nothing to contend with, so the call is a no-op.
    /// </summary>
    public void Acquire()
    {
        SchedThread? currentThread = SchedulerManager.IsReady
            ? SchedulerManager.GetCpuState(SchedulerManager.GetCurrentCpuId()).CurrentThread
            : null;

        if (currentThread == null)
        {
            return;
        }

        // The idle thread is the scheduler's fallback (PickNext ?? IdleThread):
        // blocking it only gets it resurrected on the next tick, which re-runs
        // this loop — every pass calls OnThreadBlocked again and drifts
        // TotalTickets (and Release's ReadyThread would insert the idle
        // thread into the run queue it is supposed to back-stop). Unlike
        // InterruptEvent the context can't be nulled — Release checks
        // ownership — so an idle-thread caller spin-acquires instead:
        // ownership stays real, it just never parks in _waitingThreads.
        // Interrupts stay enabled between attempts, so the timer keeps
        // preempting to the (runnable) holder until it releases.
        if ((currentThread.Flags & ThreadFlags.IdleThread) != 0)
        {
            while (true)
            {
                using (_lockGuard.AcquireIrqSafe())
                {
                    if (_ownerThread == null)
                    {
                        _ownerThread = currentThread;
                        _recursionDepth = 1;
                        return;
                    }

                    if (_ownerThread == currentThread)
                    {
                        _recursionDepth++;
                        return;
                    }
                }
            }
        }

        while (true)
        {
            // IRQ-safe for two reasons: a holder preempted mid-section
            // would deadlock any other thread spinning on the state lock
            // with interrupts masked, and blocking must be atomic with
            // the wait-queue insertion — a Release() racing between the
            // insertion and BlockThread would ready a still-Running
            // thread whose subsequent BlockThread buries the wakeup
            // forever (lost-wakeup race, same shape as InterruptEvent).
            using (_lockGuard.AcquireIrqSafe())
            {
                if (_ownerThread == null)
                {
                    _ownerThread = currentThread;
                    _recursionDepth = 1;
                    return;
                }

                if (_ownerThread == currentThread)
                {
                    _recursionDepth++;
                    return;
                }

                if (!_waitingThreads.Contains(currentThread))
                {
                    _waitingThreads.Add(currentThread);
                }

                SchedulerManager.BlockThread(currentThread.CpuId, currentThread);
            }

            InternalCpu.Halt();
        }
    }

    /// <summary>
    /// Tries to acquire the mutex without blocking.
    /// </summary>
    /// <returns>true if acquired, false if held by another thread.</returns>
    public bool TryAcquire()
    {
        SchedThread? currentThread = SchedulerManager.IsReady
            ? SchedulerManager.GetCpuState(SchedulerManager.GetCurrentCpuId()).CurrentThread
            : null;

        if (currentThread == null)
        {
            return true;
        }

        using (_lockGuard.AcquireIrqSafe())
        {
            if (_ownerThread == null)
            {
                _ownerThread = currentThread;
                _recursionDepth = 1;
                return true;
            }

            if (_ownerThread == currentThread)
            {
                _recursionDepth++;
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Releases the mutex. Must be called by the thread that holds it.
    /// </summary>
    /// <remarks>
    /// Recursive locks must be released the same number of times they were acquired.
    /// </remarks>
    public void Release()
    {
        SchedThread? currentThread = SchedulerManager.IsReady
            ? SchedulerManager.GetCpuState(SchedulerManager.GetCurrentCpuId()).CurrentThread
            : null;

        if (currentThread == null)
        {
            return;
        }

        SchedThread? toReady = null;
        using (_lockGuard.AcquireIrqSafe())
        {
            if (_ownerThread != currentThread)
            {
                return; // Error: not the owner
            }

            _recursionDepth--;

            if (_recursionDepth == 0)
            {
                _ownerThread = null;

                // Wake up one waiting thread if any
                if (_waitingThreads.Count > 0)
                {
                    toReady = _waitingThreads[0];
                    _waitingThreads.RemoveAt(0);
                }
            }
        }

        if (toReady != null)
        {
            SchedulerManager.ReadyThread(toReady.CpuId, toReady);
        }
    }

    /// <summary>
    /// Gets whether this mutex is currently locked by any thread.
    /// </summary>
    public bool IsLocked
    {
        get
        {
            using (_lockGuard.AcquireIrqSafe())
            {
                return _ownerThread != null;
            }
        }
    }

    /// <summary>
    /// Gets the current owner thread.
    /// </summary>
    public SchedThread? OwnerThread
    {
        get
        {
            using (_lockGuard.AcquireIrqSafe())
            {
                return _ownerThread;
            }
        }
    }

    /// <summary>
    /// Gets the number of waiting threads.
    /// </summary>
    public int WaitingThreadCount
    {
        get
        {
            using (_lockGuard.AcquireIrqSafe())
            {
                return _waitingThreads.Count;
            }
        }
    }

    public void Dispose()
    {
        while (true)
        {
            SchedThread waitingThread;
            using (_lockGuard.AcquireIrqSafe())
            {
                if (_waitingThreads.Count == 0)
                {
                    break;
                }

                waitingThread = _waitingThreads[0];
                _waitingThreads.RemoveAt(0);
            }

            SchedulerManager.ReadyThread(waitingThread.CpuId, waitingThread);
        }

        _ownerThread = null;
        _recursionDepth = 0;
        GC.SuppressFinalize(this);
    }
}
