using System;
using System.Runtime.InteropServices;

namespace Cosmos.Kernel.HAL.Devices.Graphic.SVGAII;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SVGA3dCmdUpdateGBImage
{
    public SVGA3dSurfaceImageId image;
    public SVGA3dBox box;
}
