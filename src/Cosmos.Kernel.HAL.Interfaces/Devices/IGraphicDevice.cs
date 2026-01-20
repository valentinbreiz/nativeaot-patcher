// This code is licensed under MIT license (see LICENSE for details)

using System;

namespace Cosmos.Kernel.HAL.Interfaces.Devices;

/// <summary>
/// Interface for graphic devices.
/// </summary>
public interface IGraphicDevice
{
    /// <summary>
    /// Initialize the graphic device.
    /// </summary>
    void Initialize();

    /// <summary>
    /// Clear the screen with a solid color.
    /// </summary>
    /// <param name="color">ARGB color value.</param>
    void ClearScreen(uint color);

    /// <summary>
    /// Draw a single pixel.
    /// </summary>
    /// <param name="color">ARGB color value.</param>
    /// <param name="x">X coordinate.</param>
    /// <param name="y">Y coordinate.</param>
    void DrawPixel(uint color, int x, int y);

    /// <summary>
    /// Copy a buffer of pixels to a rectangular region.
    /// </summary>
    /// <param name="pixels">Pixel data as ARGB values.</param>
    /// <param name="x">Destination X coordinate.</param>
    /// <param name="y">Destination Y coordinate.</param>
    /// <param name="width">Width of the region in pixels.</param>
    /// <param name="height">Height of the region in pixels.</param>
    void CopyBuffer(ReadOnlyMemory<uint> pixels, int x, int y, int width, int height);

    /// <summary>
    /// Copy a buffer of pixels to a rectangular region.
    /// </summary>
    /// <param name="pixels">Pixel data as ARGB values (as int, common for image data).</param>
    /// <param name="x">Destination X coordinate.</param>
    /// <param name="y">Destination Y coordinate.</param>
    /// <param name="width">Width of the region in pixels.</param>
    /// <param name="height">Height of the region in pixels.</param>
    void CopyBuffer(ReadOnlyMemory<int> pixels, int x, int y, int width, int height);

    /// <summary>
    /// Swap the back buffer to the screen.
    /// </summary>
    void Swap();
}
