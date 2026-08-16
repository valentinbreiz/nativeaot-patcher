using System;

namespace Cosmos.Kernel.HAL.Devices.Graphic.SVGAII;

[Flags]
public enum RegisterEnableFlags : uint
{
    Disable = 0,
    Enable = 1 << 0,
    Hide = 1 << 1,
}