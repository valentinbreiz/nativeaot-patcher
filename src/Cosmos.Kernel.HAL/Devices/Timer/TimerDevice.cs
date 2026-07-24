// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.HAL.Interfaces.Devices;

namespace Cosmos.Kernel.HAL.Devices.Timer;

/// <summary>
/// Abstract base class for all timer devices. Maintains the software timer
/// registry that is advanced on each hardware tick of the device.
/// </summary>
public abstract class TimerDevice : Device, ITimerDevice
{
    /// <summary>Nanoseconds in one millisecond.</summary>
    protected const ulong NanosecondsPerMillisecond = 1_000_000;

    private readonly List<SoftwareTimer> _timers = new();

    /// <summary>
    /// Event handler for timer tick events.
    /// </summary>
    public TimerTickHandler? OnTick { get; set; }

    /// <summary>
    /// Initialize the timer device.
    /// </summary>
    public abstract void Initialize();

    /// <summary>
    /// Gets the timer frequency in Hz.
    /// </summary>
    public abstract uint Frequency { get; }

    /// <summary>
    /// Sets the timer frequency in Hz.
    /// </summary>
    /// <param name="frequency">Frequency in Hz.</param>
    public abstract void SetFrequency(uint frequency);

    /// <summary>
    /// Registers a software timer driven by this device's periodic tick.
    /// The timer's callback runs in interrupt context and must not block.
    /// </summary>
    /// <param name="timer">Timer to register.</param>
    public virtual void RegisterTimer(SoftwareTimer timer)
    {
        if (timer == null || timer.IsActive)
        {
            return;
        }

        using (InternalCpu.DisableInterruptsScope())
        {
            timer.SetActive(true);
            _timers.Add(timer);
        }
    }

    /// <summary>
    /// Unregisters a previously registered software timer.
    /// </summary>
    /// <param name="timer">Timer to unregister.</param>
    public virtual void UnregisterTimer(SoftwareTimer timer)
    {
        if (timer == null)
        {
            return;
        }

        using (InternalCpu.DisableInterruptsScope())
        {
            // ReferenceEquals scan (not List.Remove) to match the kernel
            // convention of avoiding EqualityComparer<T>.Default.
            for (int i = 0; i < _timers.Count; i++)
            {
                if (ReferenceEquals(_timers[i], timer))
                {
                    timer.SetActive(false);
                    _timers.RemoveAt(i);
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Advances all registered software timers and raises <see cref="OnTick"/>.
    /// Called by the driver's tick interrupt handler with the elapsed tick duration.
    /// </summary>
    /// <param name="elapsedNs">Nanoseconds elapsed since the previous tick.</param>
    protected void HandleTick(ulong elapsedNs)
    {
        for (int i = _timers.Count - 1; i >= 0; i--)
        {
            SoftwareTimer timer = _timers[i];

            if (!timer.Tick(elapsedNs))
            {
                continue;
            }

            if (!timer.Recurring)
            {
                timer.SetActive(false);
                _timers.RemoveAt(i);
            }

            timer.Invoke();
        }

        OnTick?.Invoke();
    }

    /// <summary>
    /// Blocks for the specified number of milliseconds by waiting for a
    /// one-shot software timer to fire. Requires the device tick to be running.
    /// </summary>
    /// <param name="ms">Milliseconds to wait.</param>
    public virtual void Wait(uint ms)
    {
        SoftwareTimer timer = new(static () => { }, ms * NanosecondsPerMillisecond, recurring: false);
        RegisterTimer(timer);

        while (timer.IsActive)
        {
            PlatformHAL.CpuOps?.Halt();
        }
    }
}
