using System;

namespace Cosmos.Kernel.HAL.Devices.Graphic.SVGAII;

[Flags]
public enum MobFormat : uint
{
    Invalid = 0xFFFFFFFF,
    PTDEPTH_0 = 0,
    PTDEPTH_1 = 1,
    PTDEPTH_2 = 2
}
