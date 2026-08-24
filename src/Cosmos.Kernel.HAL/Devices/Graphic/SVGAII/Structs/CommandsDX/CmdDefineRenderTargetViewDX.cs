using System;
using System.Runtime.InteropServices;

namespace Cosmos.Kernel.HAL.Devices.Graphic.SVGAII;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SVGA3dCmdDXDefineRenderTargetView
{
    public uint renderTargetViewId;
    public uint sid;
    public SVGA3dSurfaceFormat format;
    public uint resourceDimension;
    public SVGA3dRenderTargetViewDesc desc;
}
