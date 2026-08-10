using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Cosmos.Kernel.HAL.Devices.Graphic.SVGAII;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SVGACOTableDXRTViewEntry
{
    public uint sid;
    public SVGA3dSurfaceFormat format;
    public uint resourceDimension;
    public SVGA3dRenderTargetViewDesc desc;

    ulong _pad;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SVGACOTableDXDSViewEntry
{
    public uint sid;
    public SVGA3dSurfaceFormat format;
    public uint resourceDimension;
    public uint mipSlice;
    public uint firstArraySlice;
    public uint arraySize;
    public byte flags;

    byte _pad0;
    ushort _pad1;
    uint _pad2;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SVGA3dInputElementDesc
{
    public uint inputSlot;
    public uint alignedByteOffset;
    public SVGA3dSurfaceFormat format;
    public uint inputSlotClass;
    public uint instanceDayaStepRate;
    public uint inputRegister;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SVGA3dDXBlendStatePerRT
{
    public byte blendEnable;
	public byte srcBlend;
	public byte destBlend;
	public byte blendOp;
	public byte srcBlendAlpha;
	public byte destBlendAlpha;
	public byte blendOpAlpha;
	public byte renderTargetWriteMask;
	public byte logicOpEnable;
	public byte logicOp;
	public ushort pad0;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SVGA3dStreamOutputDeclarationEntry
{
    public uint outputSlot;
	public uint registerIndex;
	public byte registerMask;
	public byte pad0;
	public ushort pad1;
	public uint stream;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct SVGACOTableDXElementLayoutEntry
{
    public uint elid;
    public uint numDescs;
    public fixed byte resourceDimension[768]; // SVGA3dInputElementDesc * 32

    fixed uint _pad[62];
}
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct SVGACOTableDXBlendStateEntry
{
    public byte alphaToCoverageEnable;
    public byte independentBlendEnable;
    ushort _pad0;
    public fixed byte resourceDimension[96]; // SVGA3dDXBlendStatePerRT * 8

    fixed uint _pad[7];
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SVGACOTableDXDepthStencilEntry
{
    public byte depthEnable;
	public byte depthWriteMask;
	public byte depthFunc;
	public byte stencilEnable;
	public byte frontEnable;
	public byte backEnable;
	public byte stencilReadMask;
	public byte stencilWriteMask;

	public byte frontStencilFailOp;
	public byte frontStencilDepthFailOp;
	public byte frontStencilPassOp;
	public byte frontStencilFunc;

	public byte backStencilFailOp;
	public byte backStencilDepthFailOp;
	public byte backStencilPassOp;
	public byte backStencilFunc;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct SVGACOTableDXRasterizerStateEntry
{
    public byte fillMode;
	public byte cullMode;
	public byte frontCounterClockwise;
	public byte provokingVertexLast;
	public uint depthBias;
	public float depthBiasClamp;
	public float slopeScaledDepthBias;
	public byte depthClipEnable;
	public byte scissorEnable;
	public byte multisampleEnable;
	public byte antialiasedLineEnable;
	public float lineWidth;
	public byte lineStippleEnable;
	public byte lineStippleFactor;
	public ushort lineStipplePattern;
	public byte forcedSampleCount;
	public fixed byte mustBeZero[3];
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct SVGACOTableDXSamplerEntry
{
    public uint filter;
	public byte addressU;
	public byte addressV;
	public byte addressW;
	public byte pad0;
	public float mipLODBias;
	public byte maxAnisotropy;
	public byte comparisonFunc;
	public ushort pad1;
	public Vector4 borderColor;
	public float minLOD;
	public float maxLOD;
	public fixed uint pad2[6];
}
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct SVGACOTableDXStreamOutputEntry
{
    public uint numOutputStreamEntries;
	public fixed byte decl[1024]; // SVGA3dStreamOutputDeclarationEntry * 64
	public fixed uint streamOutputStrideInBytes[4];
	public uint rasterizedStream;
	public uint numOutputStreamStrides;
	public uint mobid;
	public uint offsetInBytes;
	public byte usesMob;
	public byte pad0;
	public ushort pad1;
	public fixed uint pad2[246];
}
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SVGACOTableDXQueryEntry
{
    public byte type;
	public ushort pad0;
	public byte state;
	public uint flags;
	public uint mobid;
	public uint offset;
}
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct SVGACOTableDXShaderEntry
{
    public SVGA3dShaderType type;
	public uint sizeInBytes;
	public uint offsetInBytes;
	public uint mobid;
	public fixed uint pad[4];
}
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SVGA3dUAViewDescBuffer
{
    public uint firstElement;
	public uint numElements;
	public uint flags;
	public uint padding0;
	public uint padding1;
}
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SVGA3dUAViewDescTex
{
    public uint mipSlice;
	public uint firstArraySlice;
	public uint arraySize;
	public uint padding0;
	public uint padding1;
}
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SVGA3dUAViewDescTex3D
{
    public uint mipSlice;
	public uint firstW;
	public uint wSize;
	public uint padding0;
	public uint padding1;
}
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct SVGA3dUAViewDesc
{
    public SVGA3dUAViewDescBuffer buffer;
    public SVGA3dUAViewDescTex tex;
    public SVGA3dUAViewDescTex3D tex3D;
}
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct SVGACOTableDXUAViewEntry
{
    public uint sid;
	public SVGA3dSurfaceFormat format;
	public uint resourceDimension;
	public SVGA3dUAViewDesc desc;
	public uint structureCount;
	public fixed uint pad[7];
}