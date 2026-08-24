using System;
using System.Runtime.InteropServices;

namespace Cosmos.Kernel.HAL.Devices.Graphic.SVGAII;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SVGA3dCmdDXDefineDepthStencilView
{
    public uint depthStencilViewId;
    public uint sid;
    public SVGA3dSurfaceFormat format;
    public uint resourceDimension;
    public uint mipSlice;
    public uint firstArraySlice;
    public uint arraySize;
    public byte flags;
    byte _pad0;
    ushort _pad1;
}
