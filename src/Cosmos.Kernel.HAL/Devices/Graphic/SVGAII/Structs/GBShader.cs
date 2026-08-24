using System;
using System.Runtime.InteropServices;

namespace Cosmos.Kernel.HAL.Devices.Graphic.SVGAII;

public unsafe struct GBShader
{
    public uint ShaderID;
    public SVGA3dShaderType Type;
    public uint MobID;
    public void* MobPtr;
}
