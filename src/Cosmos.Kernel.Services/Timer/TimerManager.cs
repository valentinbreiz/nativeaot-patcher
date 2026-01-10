// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.HAL.Interfaces.Devices;

namespace Cosmos.Kernel.Services.Timer;

/// <summary>
/// Manages system timers.
/// </summary>
public static class TimerManager
{
    private static ITimerDevice? _timer;
    private static bool _initialized;

    /// <summary>
    /// Gets whether the timer manager is initialized.
    /// </summary>
    public static bool IsInitialized => _initialized;

    /// <summary>
    /// Initializes the timer manager.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;
    }

    /// <summary>
    /// Registers a timer device with the manager.
    /// </summary>
    public static void RegisterTimer(ITimerDevice timer)
    {
        if (timer == null)
            return;

        _timer = timer;
    }

    /// <summary>
    /// Gets the current timer frequency in Hz.
    /// </summary>
    public static uint Frequency => _timer?.Frequency ?? 0;

    /// <summary>
    /// Sets the timer frequency in Hz.
    /// </summary>
    public static void SetFrequency(uint frequency)
    {
        _timer?.SetFrequency(frequency);
    }

    /// <summary>
    /// Blocks for the specified number of milliseconds.
    /// </summary>
    /// <param name="ms">Milliseconds to wait.</param>
    public static void Wait(uint ms)
    {
        _timer?.Wait(ms);
    }

    /// <summary>
    /// Gets the registered timer device.
    /// </summary>
    public static ITimerDevice? Timer => _timer;
}
