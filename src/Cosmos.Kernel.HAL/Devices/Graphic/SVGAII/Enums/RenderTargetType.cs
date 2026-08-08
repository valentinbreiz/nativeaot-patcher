using System;

namespace Cosmos.Kernel.HAL.Devices.Graphic.SVGAII;

public enum SVGA3dRenderTargetType : uint
{
    Color2 = 4,
    Color1 = 3,
    Color0 = 2,
    Depth = 0,
    stencil = 1
}
