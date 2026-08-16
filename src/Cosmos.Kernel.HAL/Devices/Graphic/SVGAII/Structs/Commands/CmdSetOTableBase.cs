using System;
using System.Runtime.InteropServices;

namespace Cosmos.Kernel.HAL.Devices.Graphic.SVGAII;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SVGA3dCmdSetOTableBase
{
    public OTableType type;
    public uint baseAddress;
    public uint sizeInBytes;
    public uint validSizeInBytes;
    public MobFormat ptDepth;
}


[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SVGA3dCmdSetOTableBase64
{
    public OTableType type;
    public ulong baseAddress;
    public uint sizeInBytes;
    public uint validSizeInBytes;
    public MobFormat ptDepth;
}