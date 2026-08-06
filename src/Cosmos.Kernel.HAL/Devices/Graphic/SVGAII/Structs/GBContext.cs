using System;
using System.Runtime.InteropServices;

namespace Cosmos.Kernel.HAL.Devices.Graphic.SVGAII;

public unsafe struct GBContext
{
    public uint ContextID;
    public uint MobID;
    public void* MobPtr;
}
