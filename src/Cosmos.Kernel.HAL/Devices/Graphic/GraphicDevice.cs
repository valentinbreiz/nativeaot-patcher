// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.HAL.Interfaces.Devices;

namespace Cosmos.Kernel.HAL.Devices.Graphic;

/// <summary>
/// Abstract base class for all timer devices.
/// </summary>
public abstract class GraphicDevice : Device, IGraphicDevice
{
    /// <summary>
    /// Initialize the timer device.
    /// </summary>
    public abstract void Initialize();
    public abstract void ClearScreen(uint color);
    public abstract void DrawPixel(uint color, int x, int y);
}
