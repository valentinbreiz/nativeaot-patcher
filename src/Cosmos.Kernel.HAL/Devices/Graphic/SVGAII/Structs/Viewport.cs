using System;
using System.Runtime.InteropServices;

namespace Cosmos.Kernel.HAL.Devices.Graphic.SVGAII;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SVGA3dViewport 
{
    public float x;
    public float y;
    public float w;
    public float h;
    public float dmin;
    public float dmax;

    public SVGA3dViewport(float x, float y, float w, float h, float dmin, float dmax)
    {
        this.x = x;
        this.y = y;
        this.w = w;
        this.h = h;
        this.dmin = dmin;
        this.dmax = dmax;
    }
}
