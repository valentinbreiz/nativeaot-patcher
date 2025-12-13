// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.HAL.Interfaces.Devices;

namespace Cosmos.Kernel.HAL.Devices.Timer;

/// <summary>
/// Abstract base class for all timer devices.
/// </summary>
public abstract class TimerDevice : Device, ITimerDevice
{
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
    /// Blocks for the specified number of milliseconds.
    /// </summary>
    /// <param name="ms">Milliseconds to wait.</param>
    public abstract void Wait(uint ms);
}
