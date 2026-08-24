using System;
using System.Runtime.InteropServices;

namespace Cosmos.Kernel.HAL.Devices.Graphic.SVGAII;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SVGA3dCmdDefineGBSurface
{
    public uint sid;
    public SVGA3dSurfaceFlags flags;
    public SVGA3dSurfaceFormat format;
    public uint numMipLevels;
    public uint multisampleCount;
    public TextureFilter autogenFilter;
    public SVGA3dSize size;
    public uint arraySize;
    uint _pad;
}
