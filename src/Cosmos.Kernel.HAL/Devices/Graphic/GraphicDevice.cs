// This code is licensed under MIT license (see LICENSE for details)

using System;
using Cosmos.Kernel.HAL.Interfaces.Devices;

namespace Cosmos.Kernel.HAL.Devices.Graphic;

/// <summary>
/// Abstract base class for all graphic devices.
/// </summary>
public abstract class GraphicDevice : Device, IGraphicDevice
{
    /// <summary>
    /// Initialize the graphic device.
    /// </summary>
    public abstract void Initialize();
    public abstract void ClearScreen(uint color);
    public abstract void DrawPixel(uint color, int x, int y);
    public abstract void CopyBuffer(ReadOnlyMemory<uint> pixels, int x, int y, int width, int height);
    public abstract void CopyBuffer(ReadOnlyMemory<int> pixels, int x, int y, int width, int height);
    public abstract void Swap();
}
