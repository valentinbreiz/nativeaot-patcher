// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.HAL.Interfaces.Devices;

/// <summary>
/// Interface for timer devices.
/// </summary>
public interface IGraphicDevice
{
    /// <summary>
    /// Initialize the timer device.
    /// </summary>
    void Initialize();
    void ClearScreen(uint color);
    void DrawPixel(uint color, int x, int y);
    void Swap();
}
