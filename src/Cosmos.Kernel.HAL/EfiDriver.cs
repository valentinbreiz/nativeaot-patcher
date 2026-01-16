// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.HAL.Devices.Graphic;

namespace Cosmos.Kernel.HAL;

/// <summary>
/// Virtio-input keyboard driver for ARM64.
/// Provides keyboard input via virtio-input device on QEMU virt machine.
/// </summary>
public unsafe class EfiDriver : GraphicDevice
{
    // Static callback for key events (set by KeyboardManager) - mirrors x64 PS2Keyboard
    // Note: OnKeyPressed (from base class) is set by KeyboardManager.RegisterKeyboard()

    /// <summary>
    /// Returns true if the device was successfully initialized.
    /// </summary>
    public bool IsInitialized => _initialized;

    public uint* Address;
    public uint Width;
    public uint Height;
    public uint Pitch;
    private bool _initialized;

    public EfiDriver(uint* baseAddress, uint width, uint height, uint pitch)
    {
        Address = baseAddress;
        Width = width;
        Height = height;
        Pitch = pitch;
    }

    /// <summary>
    /// Initializes the virtio keyboard device.
    /// </summary>
    public override void Initialize()
    {
        _initialized = true;
    }

    public override void DrawPixel(uint color, int x, int y)
    {
        if (x >= 0 && x < Width && y >= 0 && y < Height)
        {
            Address[y * (int)(Pitch / 4) + x] = color;
        }
    }

    public override void ClearScreen(uint color)
    {
        MemoryOp.MemSet(Address, color, (int)(Pitch / 4 * Height));
    }
}
