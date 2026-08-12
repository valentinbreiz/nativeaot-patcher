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

[StructLayout(LayoutKind.Explicit, Pack = 1)]
public struct SVGA3dRenderTargetViewDesc
{
    [FieldOffset(0)]
    public SVGA3dTVB buffer;
    [FieldOffset(0)]
    public SVGA3dTVT tex;
    [FieldOffset(0)]
    public SVGA3dTVT3D tex3D;
}