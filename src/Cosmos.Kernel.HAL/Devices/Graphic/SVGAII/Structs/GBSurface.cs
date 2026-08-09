using System;
using System.Runtime.InteropServices;

namespace Cosmos.Kernel.HAL.Devices.Graphic.SVGAII;

public unsafe struct GBSurface
{
    public SVGA3dSurfaceImageId SurfaceID;
    public uint MobID;
    public void* MobPtr;

    public SVGA3dSurfaceFlags Flags;
    public SVGA3dBox Resolution;
    public SVGA3dSurfaceFormat Format;
}
