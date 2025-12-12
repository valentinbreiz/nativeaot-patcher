// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.HAL.Devices.Input;

/// <summary>
/// Base class for all keyboard devices.
/// </summary>
public abstract class KeyboardBase : Device
{
    /// <summary>
    /// Initialize the device. Happens before the interrupt is registered, ie before the class is being used.
    /// </summary>
    public abstract void Initialize();

    /// <summary>
    /// Update keyboard LEDs.
    /// </summary>
    public abstract void UpdateLeds();

    /// <summary>
    /// Delegate for handling key press events.
    /// </summary>
    /// <param name="scanCode">The scan code of the key.</param>
    /// <param name="released">True if the key was released, false if pressed.</param>
    public delegate void KeyPressedEventHandler(byte scanCode, bool released);

    /// <summary>
    /// Event handler invoked when a key is pressed or released.
    /// </summary>
    public KeyPressedEventHandler? OnKeyPressed;

    /// <summary>
    /// Wait for a key to be pressed by halting the CPU.
    /// </summary>
    public static void WaitForKey()
    {
        PlatformHAL.CpuOps?.Halt();
    }
}
