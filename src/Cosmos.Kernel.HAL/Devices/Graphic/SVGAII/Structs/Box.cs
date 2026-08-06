using System;
using System.Runtime.InteropServices;

namespace Cosmos.Kernel.HAL.Devices.Graphic.SVGAII;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SVGA3dBox
{
    public uint x;
    public uint y;
    public uint z;
    public uint w;
    public uint h;
    public uint d;

    public SVGA3dBox(uint x, uint y, uint z, uint w, uint h,uint d)
    {
        this.x = x;
        this.y = y;
        this.z = z;
        this.w = w;
        this.h = h;
        this.d = d;
    }
}
