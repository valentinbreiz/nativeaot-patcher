// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.HAL.Devices.Input;

/// <summary>
/// PS/2 controller IO port definitions.
/// </summary>
public static class PS2Ports
{
    /// <summary>
    /// Data IO port (0x60) - Read/Write data to PS/2 devices.
    /// </summary>
    public const int Data = 0x60;

    /// <summary>
    /// Status IO port (0x64) - Read status register.
    /// </summary>
    public const int Status = 0x64;

    /// <summary>
    /// Command IO port (0x64) - Write commands to controller.
    /// </summary>
    public const int Command = 0x64;
}
