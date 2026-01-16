// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.Memory;
namespace Cosmos.Kernel.HAL.Devices.Graphic;

/// <summary>
/// UEFI Video Driver.
/// Provides video output via UEFI framebuffer.
/// </summary>
public unsafe class EfiVideoDriver : GraphicDevice
{
    /// <summary>
    /// Returns true if the device was successfully initialized.
    /// </summary>
    public bool IsInitialized => _initialized;

    /// <summary>
    /// Frame buffer memory block.
    /// </summary>
    public MemoryBlock LinearFrameBuffer;
    //public MemoryBlock LinearFrameBuffer = new MemoryBlock(0xE0000000, 1024 * 768 * 4);

    protected readonly ManagedMemoryBlock lastbuffer;

    public uint Width;
    public uint Height;
    public uint Pitch;
    public uint Stride;
    private bool _initialized;

    public EfiVideoDriver(uint* baseAddress, uint width, uint height, uint pitch)
    {
        LinearFrameBuffer = new MemoryBlock((uint)baseAddress, height * pitch);
        lastbuffer = new ManagedMemoryBlock(height * pitch);
        Width = width;
        Height = height;
        Pitch = pitch;
        Stride = 4; // Assuming 32bpp
    }

    private uint GetPointOffset(int x, int y)
    {
        return (uint)(x * Stride) + (uint)(y * Pitch);
    }

    /// <summary>
    /// Initializes the UEFI video device.
    /// </summary>
    public override void Initialize()
    {
        _initialized = true;
    }

    public override void DrawPixel(uint color, int x, int y)
    {
        uint offset = GetPointOffset(x, y);

        lastbuffer[offset] = (byte)(color & 0xFF);         // B
        lastbuffer[offset + 1] = (byte)((color >> 8) & 0xFF);  // G
        lastbuffer[offset + 2] = (byte)((color >> 16) & 0xFF); // R
        lastbuffer[offset + 3] = (byte)((color >> 24) & 0xFF); // A
    }
    
    public void ClearVRAM(int aStart, int aCount, int value)
    {
        lastbuffer.Fill(aStart, aCount, value);
    }

    public override void ClearScreen(uint color)
    {
        lastbuffer.Fill(color);
    }

    /// <summary>
    /// Swap back buffer to video memory
    /// </summary>
    public override void Swap()
    {
        LinearFrameBuffer.Copy(lastbuffer);
    }
}
