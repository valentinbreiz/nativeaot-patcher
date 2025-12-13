// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.HAL.Interfaces.Devices;

/// <summary>
/// Interface for mouse devices.
/// </summary>
public interface IMouseDevice
{
    /// <summary>
    /// Initialize the mouse device.
    /// </summary>
    void Initialize();

    /// <summary>
    /// Check if mouse data is available.
    /// </summary>
    bool DataAvailable { get; }

    /// <summary>
    /// Current X position.
    /// </summary>
    int X { get; }

    /// <summary>
    /// Current Y position.
    /// </summary>
    int Y { get; }

    /// <summary>
    /// Left button state.
    /// </summary>
    bool LeftButton { get; }

    /// <summary>
    /// Right button state.
    /// </summary>
    bool RightButton { get; }

    /// <summary>
    /// Middle button state.
    /// </summary>
    bool MiddleButton { get; }

    /// <summary>
    /// Enable the mouse.
    /// </summary>
    void Enable();

    /// <summary>
    /// Disable the mouse.
    /// </summary>
    void Disable();
}
