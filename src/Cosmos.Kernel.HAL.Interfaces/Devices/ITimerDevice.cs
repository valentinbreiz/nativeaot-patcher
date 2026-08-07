// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.HAL.Interfaces.Devices;

/// <summary>
/// Delegate for handling timer tick events.
/// </summary>
public delegate void TimerTickHandler();

/// <summary>
/// Interface for timer devices.
/// </summary>
public interface ITimerDevice
{
    /// <summary>
    /// Initialize the timer device.
    /// </summary>
    void Initialize();

    /// <summary>
    /// Gets the timer frequency in Hz.
    /// </summary>
    uint Frequency { get; }

    /// <summary>
    /// Sets the timer frequency in Hz.
    /// </summary>
    /// <param name="frequency">Frequency in Hz.</param>
    void SetFrequency(uint frequency);

    /// <summary>
    /// Blocks for the specified number of milliseconds.
    /// </summary>
    /// <param name="ms">Milliseconds to wait.</param>
    void Wait(uint ms);

    /// <summary>
    /// Registers a software timer driven by this device's periodic tick.
    /// </summary>
    /// <param name="timer">Timer to register.</param>
    void RegisterTimer(SoftwareTimer timer);

    /// <summary>
    /// Unregisters a previously registered software timer.
    /// </summary>
    /// <param name="timer">Timer to unregister.</param>
    void UnregisterTimer(SoftwareTimer timer);

    /// <summary>
    /// Event handler for timer tick events.
    /// </summary>
    TimerTickHandler? OnTick { get; set; }
}
