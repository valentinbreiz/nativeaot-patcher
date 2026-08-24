using System;
using System.Runtime.InteropServices;

namespace Cosmos.Kernel.HAL.Devices.Graphic.SVGAII;

public struct GBColorView
{
    public static GBColorView Unbound {get;} = new()
    {
        ViewID = uint.MaxValue
    };

    public GBSurface Surface;
    public uint ViewID;
}

public struct GBDepthStencilView
{
    public static GBDepthStencilView Unbound {get;} = new()
    {
        DepthPresent = false,
        StencilPresent = false,
        ViewID = uint.MaxValue
    };

    public GBSurface Surface;
    public uint ViewID;

    public bool DepthPresent;
    public bool StencilPresent;
}
