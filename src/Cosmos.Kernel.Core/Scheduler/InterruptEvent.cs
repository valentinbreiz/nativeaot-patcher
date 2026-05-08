// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core.CPU;
using SchedThread = Cosmos.Kernel.Core.Scheduler.Thread;

namespace Cosmos.Kernel.Core.Scheduler;

/// <summary>
/// One-shot binary completion event with auto-reset semantics, designed
/// to be signaled from an ISR and waited on from a normal thread.
///
/// <para><see cref="Wait"/> blocks the caller via <see cref="SchedulerManager.BlockThread"/>
/// until <see cref="Signal"/> is called. <see cref="Signal"/> only walks
/// internal state with the spinlock held and calls
/// <see cref="SchedulerManager.ReadyThread"/> — no allocation, no
/// interface dispatch — so it is safe from interrupt context.</para>
///
/// <para>Modeled after <see cref="Mutex"/> but reduced to the surface
/// drivers actually need (a producer ISR and a consumer thread). Multiple
/// consumers can wait; <see cref="Signal"/> wakes one. If signaled while
/// no consumer is waiting, the signal latches until the next
/// <see cref="Wait"/> consumes it.</para>
/// </summary>
public class InterruptEvent
{
    private SpinLock _lockGuard;
    private bool _signaled;
    private readonly List<SchedThread> _waiters;

    public InterruptEvent()
    {
        _signaled = false;
        _waiters = [];
    }

    /// <summary>
    /// Blocks the calling thread until <see cref="Signal"/> is called.
    /// Consumes the latched signal on return (auto-reset).
    /// </summary>
    public void Wait()
    {
        SchedThread? currentThread = SchedulerManager.GetCpuState(SchedulerManager.GetCurrentCpuId()).CurrentThread;
        if (currentThread == null)
        {
            return;
        }

        while (true)
        {
            _lockGuard.Acquire();

            if (_signaled)
            {
                _signaled = false;
                _lockGuard.Release();
                return;
            }

            if (!_waiters.Contains(currentThread))
            {
                _waiters.Add(currentThread);
            }
            _lockGuard.Release();

            SchedulerManager.BlockThread(currentThread.CpuId, currentThread);
            InternalCpu.Halt();
            // On wake, retry: either a Signal targeted us (we were removed
            // from _waiters) or we got readied for another reason; in
            // either case re-check state under the lock.
        }
    }

    /// <summary>
    /// Signals the event. Latches the signal so the next <see cref="Wait"/>
    /// consumes it immediately, and wakes one parked waiter (if any).
    /// Latching is required because the woken thread re-enters
    /// <see cref="Wait"/>'s loop and must see something durable rather
    /// than just "I'm not in the waiters list anymore". Safe to call
    /// from interrupt context.
    /// </summary>
    public void Signal()
    {
        _lockGuard.Acquire();
        _signaled = true;

        if (_waiters.Count > 0)
        {
            SchedThread waiter = _waiters[0];
            _waiters.RemoveAt(0);
            _lockGuard.Release();
            SchedulerManager.ReadyThread(waiter.CpuId, waiter);
            return;
        }

        _lockGuard.Release();
    }

    /// <summary>
    /// Whether the event currently has a latched signal (no waiter has
    /// consumed yet). Read without locking; for diagnostics only.
    /// </summary>
    public bool IsSignaled => _signaled;
}
