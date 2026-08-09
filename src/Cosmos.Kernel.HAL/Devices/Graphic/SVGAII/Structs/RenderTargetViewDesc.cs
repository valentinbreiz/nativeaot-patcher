using System;
using System.Runtime.InteropServices;

namespace Cosmos.Kernel.HAL.Devices.Graphic.SVGAII;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SVGA3dTVB
{
    public uint firstElement;
    public uint numElements;
    public uint padding0;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SVGA3dTVT
{
    public uint mipSlice;
    public uint firstArraySlice;
    public uint arraySize;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SVGA3dTVT3D
{
    public uint mipSlice;
    public uint firstW;
    public uint wSize;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SVGA3dRenderTargetViewDesc
{
    public SVGA3dTVB buffer;
    public SVGA3dTVT tex;
    public SVGA3dTVT3D tex3D;
}