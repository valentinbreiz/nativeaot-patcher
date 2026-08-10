using System;
using System.Runtime.InteropServices;

namespace Cosmos.Kernel.HAL.Devices.Graphic.SVGAII;

public static class SVGA3dDXLimits
{
    public const int MAX_VERTEXBUFFERS = 32;
    public const int MAX_RENDER_TARGETS = 8;
    public const int MAX_SOTARGETS = 4;
    public const int MAX_VIEWPORTS = 16;
    public const int MAX_SCISSORRECTS = 16;
    public const int MAX_CONSTBUFFERS = 16;
    public const int MAX_SRVIEWS = 128;
    public const int MAX_SAMPLERS = 16;
    public const int NUM_SHADERTYPE = 6;
    public const int MAX_QUERY = 64;
    public const int COTABLE_MAX = 11;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SVGA3dBufferBinding
{
    public uint bufferId;
    public uint stride;
    public uint offset;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SVGA3dConstantBufferBinding
{
    public uint sid;
    public uint offsetInBytes;
    public uint sizeInBytes;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct SVGADXInputAssemblyMobFormat
{
    public uint layoutId;
    public fixed byte vertexBuffersRaw[SVGA3dDXLimits.MAX_VERTEXBUFFERS * 12]; // SVGA3dBufferBinding is 12 bytes
    public uint indexBufferSid;
    public uint pad;
    public uint indexBufferOffset;
    public uint indexBufferFormat;
    public uint topology;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SVGASignedRect
{
    public int left;
    public int top;
    public int right;
    public int bottom;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SVGA3dCOTableData
{
    public uint mobid;
    public uint validSizeInBytes;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct SVGADXContextMobFormat
{
    public SVGADXInputAssemblyMobFormat inputAssembly;

    public uint blendStateId;
    public fixed float blendFactor[4];
    public uint sampleMask;
    public uint depthStencilStateId;
    public uint stencilRef;
    public uint rasterizerStateId;
    public uint depthStencilViewId;
    public fixed uint renderTargetViewIds[SVGA3dDXLimits.MAX_RENDER_TARGETS];

    public fixed uint pad0[8];

    public fixed uint streamOutTargets[SVGA3dDXLimits.MAX_SOTARGETS];
    public uint soid;

    public fixed uint pad1[10];

    public uint uavSpliceIndex;
    public byte numViewports;
    public byte numScissorRects;
    public ushort pad2;
    public fixed uint pad3[3];

    public fixed byte viewportsRaw[SVGA3dDXLimits.MAX_VIEWPORTS * 24];
    public fixed uint pad4[32];

    public fixed byte scissorRectsRaw[SVGA3dDXLimits.MAX_SCISSORRECTS * 16];
    public fixed uint pad5[64];

    public uint predicationQueryID;
    public uint predicationValue;

    public uint shaderIfaceMobid;
    public uint shaderIfaceOffset;

    public fixed byte shaderStateRaw[
        SVGA3dDXLimits.NUM_SHADERTYPE *
        (4 + SVGA3dDXLimits.MAX_CONSTBUFFERS * 12
           + SVGA3dDXLimits.MAX_SRVIEWS * 4
           + SVGA3dDXLimits.MAX_SAMPLERS * 4)
    ];

    public fixed uint pad6[26];

    public fixed uint queryID[SVGA3dDXLimits.MAX_QUERY];

    public fixed byte cotablesRaw[SVGA3dDXLimits.COTABLE_MAX * 8];

    public fixed uint pad7[64];

    public fixed uint uaViewIds[8];
    public fixed uint csuaViewIds[8];

    public fixed uint pad8[188];
}

public static class SVGA3dContextLimits
{
    public const int RT_MAX = 10;
    public const int RS_MAX = 99;
    public const int MAX_CLIP_PLANES = 6;
    public const int TRANSFORM_MAX = 15;
    public const int NUM_LIGHTS = 8;
    public const int NUM_SHADERTYPE_PREDX = 2;
    public const int CONSTINTREG_MAX = 16;
    public const int MAX_VERTEX_ARRAYS = 32;
    public const int NUM_TEXTURE_UNITS = 32;
    public const int TS_CONSTANT_PLUS_1 = 31;
    public const int CONSTREG_MAX = 256;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct SVGAGBContextData
{
    public uint viewportX, viewportY, viewportW, viewportH;

    public uint scissorX, scissorY, scissorW, scissorH;

    public float zRangeMin, zRangeMax;

    public fixed uint renderTargets[SVGA3dContextLimits.RT_MAX * 3];

    public fixed byte decl1Raw[4 * 6];

    public fixed uint renderStates[SVGA3dContextLimits.RS_MAX];

    public fixed byte decl2Raw[18 * 6];

    public fixed uint pad0[2];

    public fixed byte materialRaw[72];

    public fixed float clipPlanes[SVGA3dContextLimits.MAX_CLIP_PLANES * 4];

    public fixed float matrices[SVGA3dContextLimits.TRANSFORM_MAX * 16];

    public fixed uint lightEnabled[SVGA3dContextLimits.NUM_LIGHTS];

    public fixed byte lightDataRaw[SVGA3dContextLimits.NUM_LIGHTS * 116];

    public fixed uint shaders[SVGA3dContextLimits.NUM_SHADERTYPE_PREDX];

    public fixed byte decl3Raw[10 * 6];

    public fixed uint pad1[3];

    public uint occQueryActive;
    public uint occQueryValue;

    public fixed int pShaderIValues[SVGA3dContextLimits.CONSTINTREG_MAX * 4];
    public fixed int vShaderIValues[SVGA3dContextLimits.CONSTINTREG_MAX * 4];

    public ushort pShaderBValues;
    public ushort vShaderBValues;

    public fixed byte streamsRaw[SVGA3dContextLimits.MAX_VERTEX_ARRAYS * 10];

    public fixed uint divisors[SVGA3dContextLimits.MAX_VERTEX_ARRAYS];

    public uint numVertexDecls;
    public uint numVertexStreams;
    public uint numVertexDivisors;

    public fixed uint pad2[30];

    public fixed uint tsColorKey[SVGA3dContextLimits.NUM_TEXTURE_UNITS];

    public fixed uint textureStages[SVGA3dContextLimits.NUM_TEXTURE_UNITS * SVGA3dContextLimits.TS_CONSTANT_PLUS_1];

    public fixed uint tsColorKeyEnable[SVGA3dContextLimits.NUM_TEXTURE_UNITS];

    public fixed float pShaderFValues[SVGA3dContextLimits.CONSTREG_MAX * 4];
    public fixed float vShaderFValues[SVGA3dContextLimits.CONSTREG_MAX * 4];
}