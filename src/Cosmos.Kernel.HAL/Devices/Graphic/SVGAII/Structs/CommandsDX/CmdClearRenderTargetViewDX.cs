using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Cosmos.Kernel.HAL.Devices.Graphic.SVGAII;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SVGA3dCmdDXClearRenderTargetView
{
    public uint renderTargetViewId;
    public Vector4 rgba;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SVGA3dCmdDXClearDepthStencilView
{
    public ushort flags;
    public ushort stencil;
    public uint depthStencilViewId;
    public float depth;
}
