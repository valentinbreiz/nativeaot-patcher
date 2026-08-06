using System;
using Cosmos.Kernel.Core.Memory;

namespace Cosmos.Kernel.HAL.Devices.Graphic.SVGAII;

/// <summary>
/// SVGA3D command layer on top of <see cref="SvgaIIDriver"/>: surfaces,
/// contexts, shaders, render state and direct MOB transfers, all submitted through
/// the driver's command FIFO. Only meaningful when the device negotiated 3D
/// support (<see cref="SvgaIIDriver.Is3DEnabled"/>) — QEMU's vmware-svga
/// exposes no 3D capability, so this layer is only exercised on real VMware.
/// </summary>
public unsafe class VMWareSVGAII3D
{
    private readonly SvgaIIDriver _driver;

    public bool Is3DEnabled => _driver.Is3DEnabled;
    public uint HW3DVer => _driver.HW3DVer;

    public VMWareSVGAII3D(SvgaIIDriver driver)
    {
        _driver = driver;

        s_dmaSize = _driver.VideoMemory.Size / 8;
        uint dmaStartOffset = (_driver.VideoMemory.Size - s_dmaSize) & ~3u;

        s_dmaStart = new SVGAGuestPtr { gmrId = SVGA_GMR_FRAMEBUFFER, offset = dmaStartOffset };
        s_nextPtr.offset = s_dmaStart.offset;
    }

    private uint _contextId;
    private GBContext? _boundContextId = null;

    private uint GetNextContextId() => ++_contextId;

    private uint CurrentContextId => _boundContextId?.ContextID 
        ?? throw new InvalidOperationException("No context is currently bound! Call BindContext first.");

    private uint _surfaceId;
    private GBSurface? _boundSurfaceId = null;

    private uint GetNextSurfaceId() => ++_surfaceId;

    private uint _mobIdCounter = 0;
    private uint GetNextMobId() => ++_mobIdCounter;

    private uint _persistentMobOffset = 0;

    private bool _fifoFenceSupported;
    private uint _guestFenceCounter = 1;

    private uint _lastDMASize = 0;

    private int[] _imagebuffer = [];
    private uint _lastFence = 1;

    private uint _shaderIdVS = 0;
    private uint _shaderIdPS = 0;

    private uint GetNextShaderId(SVGA3dShaderType type)
    {
        switch (type)
        {
            case SVGA3dShaderType.SVGA3D_SHADERTYPE_VS: return _shaderIdVS++;
            case SVGA3dShaderType.SVGA3D_SHADERTYPE_PS: return _shaderIdPS++;
            default: return 0;
        }
    }

    private void SyncToFence(uint fence)
    {
        if (_fifoFenceSupported)
        {
            while (_driver.ReadFifo3D(Register3D.SVGA_FIFO_FENCE) < fence) { }
        }
        else
        {
            _driver.WriteRegister(Register.Sync, 1);
            while (_driver.ReadRegister(Register.Busy) != 0) { }
        }
    }

    private uint InsertFence()
    {
        uint fence = ++_guestFenceCounter;

        if (_fifoFenceSupported)
        {
            _driver.WriteFifo3D(Register3D.SVGA_FIFO_FENCE, fence);
        }
        else
        {
            _driver.WriteRegister(Register.Sync, fence);
        }

        return fence;
    }

    public void* ReserveFIFO3D(uint cmd, uint cmdSize)
    {
        SVGA3dCmdHeader* header;

        header = (SVGA3dCmdHeader*)_driver.ReserveFIFO((uint)sizeof(SVGA3dCmdHeader) + cmdSize);
        header->id = cmd;
        header->size = cmdSize;

        return &header[1];
    }

    #region Context Management
    public GBContext DefineContext()
    {
        uint cid = GetNextContextId();

        SVGA3dCmdDefineContext* cmd;
        cmd = (SVGA3dCmdDefineContext*)ReserveFIFO3D((uint)(CheckGBCached() ? FIFOCommand.SVGA_3D_CMD_DEFINE_GB_CONTEXT : FIFOCommand.DEFINE_CONTEXT), (uint)sizeof(SVGA3dCmdDefineContext));
        cmd->cid = cid;

        void* mobPtr = (void*)0;

        var mobid = CheckGBCached() ? DefineGBMob(4096, out mobPtr, out _) : 0;

        var ctx = new GBContext { ContextID = cid, MobID = mobid, MobPtr = mobPtr };
        BindContext(ctx);

        return ctx;
    }

    public GBContext BindContext(GBContext context)
    {
        if (_boundContextId.HasValue && _boundContextId.Value.ContextID == context.ContextID)
        {
            return _boundContextId.Value;
        }

        if (CheckGBCached())
        {
            SVGA3dCmdBindGBContext* cmd;
            cmd = (SVGA3dCmdBindGBContext*)ReserveFIFO3D((uint)FIFOCommand.SVGA_3D_CMD_BIND_GB_CONTEXT, (uint)sizeof(SVGA3dCmdBindGBContext));
            cmd->cid = context.ContextID;
            cmd->mobid = context.MobID;
            cmd->validContents = 0;
        }

        _boundContextId = context;
        return _boundContextId.Value;
    }

    public void DestroyContext(GBContext context)
    {
        uint* cmd = (uint*)ReserveFIFO3D((uint)(CheckGBCached() ? FIFOCommand.SVGA_3D_CMD_DESTROY_GB_CONTEXT : FIFOCommand.DESTROY_CONTEXT), sizeof(uint));
        *cmd = context.ContextID;

        if (context.MobID != 0)
        {
            DestroyGBMob(context.MobID);
        }
    }

    #endregion
    #region Surface Management

    public GBSurface DefineSurface(uint width, uint height, uint depth, SVGA3dSurfaceFormat format, uint mips = 1)
    {
        uint sid = GetNextSurfaceId();

        if (CheckGBCached())
        {
            uint mobSize = CalculateSurfaceMobSize(width, height, depth, mips, format);
            uint mobid = DefineGBMob(mobSize, out void* mobPtr, out _);

            SVGA3dCmdDefineGBSurface* cmd = (SVGA3dCmdDefineGBSurface*)ReserveFIFO3D(
                (uint)FIFOCommand.SVGA_3D_CMD_DEFINE_GB_SURFACE,
                (uint)sizeof(SVGA3dCmdDefineGBSurface));

            cmd->sid = sid;
            cmd->flags = 0;
            cmd->format = format;
            cmd->numMipLevels = mips;
            cmd->multisampleCount = 0;
            cmd->autogenFilter = 0;
            cmd->size.width = width;
            cmd->size.height = height;
            cmd->size.depth = depth;

            SVGA3dCmdBindGBSurface* bindCmd = (SVGA3dCmdBindGBSurface*)ReserveFIFO3D(
                (uint)FIFOCommand.SVGA_3D_CMD_BIND_GB_SURFACE,
                (uint)sizeof(SVGA3dCmdBindGBSurface));

            bindCmd->sid = sid;
            bindCmd->mobid = mobid;

            return new GBSurface
            {
                SurfaceID = new() { sid = sid, face = 0, mipmap = 0 },
                MobID = mobid,
                MobPtr = mobPtr
            };
        }
        else
        {
            SVGA3dCmdDefineSurface* cmd = (SVGA3dCmdDefineSurface*)ReserveFIFO3D(
                (uint)FIFOCommand.DEFINE_SURFACE,
                (uint)sizeof(SVGA3dCmdDefineSurface) + (uint)(mips * sizeof(SVGA3dSize)));

            cmd->sid = sid;
            cmd->flags = 0;
            cmd->format = format;

            MemoryOp.MemSet((byte*)&cmd->face[0], 0, sizeof(uint) * 6);
            cmd->face[0] = mips;

            SVGA3dSize* mipSizes = (SVGA3dSize*)&cmd[1];

            for (uint i = 0; i < mips; i++)
            {
                mipSizes[i].width = Math.Max(1u, width >> (int)i);
                mipSizes[i].height = Math.Max(1u, height >> (int)i);
                mipSizes[i].depth = Math.Max(1u, depth >> (int)i);
            }

            return new GBSurface
            {
                SurfaceID = new() { sid = sid, face = 0, mipmap = 0 },
                MobID = 0,
                MobPtr = null
            };
        }
    }

    public GBSurface BindSurface(GBSurface surface)
    {
        if (CheckGBCached())
        {
            SVGA3dCmdBindGBSurface* cmd = (SVGA3dCmdBindGBSurface*)ReserveFIFO3D((uint)FIFOCommand.SVGA_3D_CMD_BIND_GB_SURFACE, (uint)sizeof(SVGA3dCmdBindGBSurface));
            cmd->sid = surface.SurfaceID.sid;
            cmd->mobid = surface.MobID;
        }

        _boundSurfaceId = surface;
        return _boundSurfaceId.Value;
    }

    public void DestroySurface(GBSurface surface)
    {
        uint* cmd = (uint*)ReserveFIFO3D((uint)(CheckGBCached() ? FIFOCommand.SVGA_3D_CMD_DESTROY_GB_SURFACE : FIFOCommand.DESTROY_SURFACE), sizeof(uint));
        *cmd = surface.SurfaceID.sid;

        if (surface.MobID != 0)
        {
            DestroyGBMob(surface.MobID);
        }
    }

    #endregion
    #region Shaders

    public GBShader DefineShader(SVGA3dShaderType type, byte[] bytecode)
    {
        uint shid = GetNextShaderId(type);

        if (CheckGBCached())
        {
            uint size = (uint)bytecode.Length;
            uint mobid = DefineGBMob(size, out void* mobPtr, out _);

            fixed (byte* bytecodePtr = bytecode)
            {
                MemoryOp.MemCopy((byte*)mobPtr, bytecodePtr, bytecode.Length);
            }

            SVGA3dCmdDefineGBShader* cmd = (SVGA3dCmdDefineGBShader*)ReserveFIFO3D((uint)FIFOCommand.SVGA_3D_CMD_DEFINE_GB_SHADER, (uint)sizeof(SVGA3dCmdDefineGBShader));
            cmd->shid = shid;
            cmd->type = type;
            cmd->sizeInBytes = size;

            var shader = new GBShader { ShaderID = shid, MobID = mobid, Type = type, MobPtr = mobPtr };
            BindShader(shader);
            return shader;
        }
        else
        {
            SVGA3dCmdDefineShader* cmd = (SVGA3dCmdDefineShader*)ReserveFIFO3D((uint)FIFOCommand.SHADER_DEFINE, (uint)sizeof(SVGA3dCmdDefineShader) + (uint)bytecode.Length);
            cmd->cid = CurrentContextId;
            cmd->shid = shid;
            cmd->type = type;

            fixed (byte* bytecodePtr = bytecode)
            {
                MemoryOp.MemCopy((byte*)&cmd[1], bytecodePtr, bytecode.Length);
            }

            return new GBShader { ShaderID = shid, MobID = 0, Type = type, MobPtr = null };
        }
    }

    public GBShader BindShader(GBShader shader)
    {
        if (CheckGBCached())
        {
            SVGA3dCmdBindGBShader* cmd = (SVGA3dCmdBindGBShader*)ReserveFIFO3D((uint)FIFOCommand.SVGA_3D_CMD_BIND_GB_SHADER, (uint)sizeof(SVGA3dCmdBindGBShader));
            cmd->shid = shader.ShaderID;
            cmd->mobid = shader.MobID;
            cmd->offsetInBytes = 0;
        }
        return shader;
    }

    public void SetShader(GBShader shader)
    {
        SVGA3dCmdSetShader* cmd = (SVGA3dCmdSetShader*)ReserveFIFO3D((uint)FIFOCommand.SET_SHADER, (uint)sizeof(SVGA3dCmdSetShader));
        cmd->cid = CurrentContextId;
        cmd->type = shader.Type;
        cmd->shid = shader.ShaderID;
    }

    public void SetShaderUniform<T>(uint reg, SVGA3dShaderType type, SVGA3dShaderConstType ctype, T value) where T : unmanaged
    {
        SVGA3dCmdSetShaderConst* cmd = (SVGA3dCmdSetShaderConst*)ReserveFIFO3D((uint)FIFOCommand.SET_SHADER_CONST, (uint)sizeof(SVGA3dCmdSetShaderConst));
        cmd->cid = CurrentContextId;
        cmd->reg = reg;
        cmd->type = type;
        cmd->ctype = ctype;
        cmd->values[0] = 0; cmd->values[1] = 0; cmd->values[2] = 0; cmd->values[3] = 0;

        byte* src = (byte*)&value;
        byte* dst = (byte*)cmd->values;
        int size = Math.Min(sizeof(T), 16);
        for (int i = 0; i < size; i++)
        {
            dst[i] = src[i];
        }
    }

    public void DestroyShader(GBShader shader)
    {
        if (CheckGBCached())
        {
            SVGA3dCmdDestroyGBShader* cmd = (SVGA3dCmdDestroyGBShader*)ReserveFIFO3D((uint)FIFOCommand.SVGA_3D_CMD_DESTROY_GB_SHADER, (uint)sizeof(SVGA3dCmdDestroyGBShader));
            cmd->shid = shader.ShaderID;
            DestroyGBMob(shader.MobID);
        }
        else
        {
            SVGA3dCmdDestroyShader* cmd = (SVGA3dCmdDestroyShader*)ReserveFIFO3D((uint)FIFOCommand.DESTROY_SHADER, (uint)sizeof(SVGA3dCmdDestroyShader));
            cmd->cid = CurrentContextId;
            cmd->shid = shader.ShaderID;
            cmd->type = shader.Type;
        }
    }

    #endregion
    #region  State & Render Commands

    public void SetRenderTarget(SVGA3dRenderTargetType type, GBSurface target)
    {
        SVGA3dCmdSetRenderTarget* cmd = (SVGA3dCmdSetRenderTarget*)ReserveFIFO3D((uint)FIFOCommand.SET_RENDER_TARGET, (uint)sizeof(SVGA3dCmdSetRenderTarget));
        cmd->cid = CurrentContextId;
        cmd->type = type;
        cmd->target = target.SurfaceID;
    }

    public void SetViewport(SVGA3dRect rect)
    {
        SVGA3dCmdSetViewport* cmd = (SVGA3dCmdSetViewport*)ReserveFIFO3D((uint)FIFOCommand.SET_VIEWPORT, (uint)sizeof(SVGA3dCmdSetViewport));
        cmd->cid = CurrentContextId;
        cmd->rect = rect;
    }

    public void SetDepthRange(float min, float max)
    {
        SVGA3dCmdSetZRange* cmd = (SVGA3dCmdSetZRange*)ReserveFIFO3D((uint)FIFOCommand.SET_ZRANGE, (uint)sizeof(SVGA3dCmdSetZRange));
        cmd->cid = CurrentContextId;
        cmd->range.min = min;
        cmd->range.max = max;
    }

    private void BeginClear3D(ClearFlags flags, uint color, float depth, uint stencil, SVGA3dRect** rects, uint numRects)
    {
        SVGA3dCmdClear* cmd = (SVGA3dCmdClear*)ReserveFIFO3D((uint)FIFOCommand.CLEAR, (uint)sizeof(SVGA3dCmdClear) + (uint)(numRects * sizeof(SVGA3dRect)));
        cmd->cid = CurrentContextId;
        cmd->flag = flags;
        cmd->color = color;
        cmd->depth = depth;
        cmd->stencil = stencil;
        *rects = (SVGA3dRect*)&cmd[1];
    }

    public void Clear3D(ClearFlags flags, SVGA3dRect ClearRect, uint color = 0, float depth = 1, uint stencil = 0)
    {
        SVGA3dRect* rect;
        BeginClear3D(flags, color, depth, stencil, &rect, 1);
        rect->x = ClearRect.x;
        rect->y = ClearRect.y;
        rect->w = ClearRect.w;
        rect->h = ClearRect.h;
    }

    private void BeginSetRenderState(SVGA3dRenderState** states, uint numstates)
    {
        SVGA3dCmdSetRenderState* cmd = (SVGA3dCmdSetRenderState*)ReserveFIFO3D((uint)FIFOCommand.SETRENDERSTATE, (uint)(sizeof(SVGA3dCmdSetRenderState) + sizeof(SVGA3dRenderState) * numstates));
        cmd->cid = CurrentContextId;
        *states = (SVGA3dRenderState*)&cmd[1];
    }

    public void SetRenderState(SVGA3dRenderState[] states)
    {
        SVGA3dRenderState* rs;
        BeginSetRenderState(&rs, (uint)states.Length);

        fixed (SVGA3dRenderState* statesPtr = &states[0])
        {
            MemoryOp.MemCopy((byte*)rs, (byte*)statesPtr, sizeof(SVGA3dRenderState) * states.Length);
        }
    }

    private void BeginSetTextureState(SVGA3dTextureState** states, uint numStates)
    {
        SVGA3dCmdSetTextureState* cmd = (SVGA3dCmdSetTextureState*)ReserveFIFO3D((uint)FIFOCommand.SETTEXTURESTATE, (uint)(sizeof(SVGA3dCmdSetTextureState) + sizeof(SVGA3dTextureState) * numStates));
        cmd->cid = CurrentContextId;
        *states = (SVGA3dTextureState*)&cmd[1];
    }

    public void SetTextureState(SVGA3dTextureState[] states)
    {
        SVGA3dTextureState* ts;
        BeginSetTextureState(&ts, (uint)states.Length);

        fixed (SVGA3dTextureState* statesPtr = &states[0])
        {
            MemoryOp.MemCopy((byte*)ts, (byte*)statesPtr, sizeof(SVGA3dTextureState) * states.Length);
        }
    }

    private void BeginDrawPrimitives(SVGA3dVertexDecl** decls, uint numVertexDecls, SVGA3dPrimitiveRange** ranges, uint numRanges)
    {
        uint declSize = (uint)sizeof(SVGA3dVertexDecl) * numVertexDecls;
        uint rangeSize = (uint)sizeof(SVGA3dPrimitiveRange) * numRanges;

        SVGA3dCmdDrawPrimitives* cmd = (SVGA3dCmdDrawPrimitives*)ReserveFIFO3D((uint)FIFOCommand.DRAW_PRIMITIVES, (uint)sizeof(SVGA3dCmdDrawPrimitives) + declSize + rangeSize);
        cmd->cid = CurrentContextId;
        cmd->numVertexDecls = numVertexDecls;
        cmd->numRanges = numRanges;

        SVGA3dVertexDecl* declArray = (SVGA3dVertexDecl*)&cmd[1];
        SVGA3dPrimitiveRange* rangeArray = (SVGA3dPrimitiveRange*)&declArray[numVertexDecls];

        MemoryOp.MemSet((byte*)declArray, 0, (int)declSize);
        MemoryOp.MemSet((byte*)rangeArray, 0, (int)rangeSize);

        *decls = declArray;
        *ranges = rangeArray;
    }

    public void DrawPrimitives(SVGA3dVertexDecl[] decls, SVGA3dPrimitiveRange[] ranges)
    {
        SVGA3dVertexDecl* vdecls;
        SVGA3dPrimitiveRange* pranges;
        BeginDrawPrimitives(&vdecls, (uint)decls.Length, &pranges, (uint)ranges.Length);

        fixed (SVGA3dVertexDecl* statesPtr = &decls[0])
        {
            MemoryOp.MemCopy((byte*)vdecls, (byte*)statesPtr, sizeof(SVGA3dVertexDecl) * decls.Length);
        }
        fixed (SVGA3dPrimitiveRange* statesPtr = &ranges[0])
        {
            MemoryOp.MemCopy((byte*)pranges, (byte*)statesPtr, sizeof(SVGA3dPrimitiveRange) * ranges.Length);
        }
    }

    public void SetTransform<T>(SVGA3dTransformType type, T matrix4x4) where T : unmanaged
    {
        if (sizeof(T) != 16 * sizeof(float))
        {
            throw new ArgumentException("Matrix must be 4x4 float");
        }

        SVGA3dCmdSetTransform* cmd = (SVGA3dCmdSetTransform*)ReserveFIFO3D((uint)FIFOCommand.SETTRANSFORM, (uint)sizeof(SVGA3dCmdSetTransform));
        cmd->cid = CurrentContextId;
        cmd->type = type;

        MemoryOp.MemCopy((byte*)&cmd->matrix[0], (byte*)&matrix4x4, sizeof(float) * 16);
    }

    public void SetLightEnable(uint index, bool enabled)
    {
        SVGA3dCmdSetLightEnabled* cmd = (SVGA3dCmdSetLightEnabled*)ReserveFIFO3D((uint)FIFOCommand.SETLIGHTENABLE, (uint)sizeof(SVGA3dCmdSetLightEnabled));
        cmd->cid = CurrentContextId;
        cmd->index = index;
        cmd->enabled = enabled ? 1u : 0u;
    }

    public void SetLightData(uint index, SVGA3dLightData data)
    {
        SVGA3dCmdSetLightData* cmd = (SVGA3dCmdSetLightData*)ReserveFIFO3D((uint)FIFOCommand.SETLIGHTDATA, (uint)sizeof(SVGA3dCmdSetLightData));
        cmd->cid = CurrentContextId;
        cmd->index = index;
        MemoryOp.MemCopy((byte*)&cmd->data, (byte*)&data, sizeof(SVGA3dLightData));
    }

    public void SetMaterial(Face face, SVGA3dMaterial material)
    {
        SVGA3dCmdSetMaterial* cmd = (SVGA3dCmdSetMaterial*)ReserveFIFO3D((uint)FIFOCommand.SETMATERIAL, (uint)sizeof(SVGA3dCmdSetMaterial));
        cmd->cid = CurrentContextId;
        cmd->face = face;
        MemoryOp.MemCopy((byte*)&cmd->material, (byte*)&material, sizeof(SVGA3dMaterial));
    }

    #endregion
    #region Direct Data Transfers & Fallbacks

    public GBSurface DefineSurfaceFromImage(int[] image, uint width, uint height)
    {
        var surface = DefineSurface(width, height, 1, SVGA3dSurfaceFormat.SVGA3D_A8R8G8B8, 1);

        if (CheckGBCached())
        {
            fixed (int* rawDataPtr = image)
            {
                MemoryOp.MemCopy((byte*)surface.MobPtr, (byte*)rawDataPtr, image.Length * sizeof(int));
            }

            SVGA3dCmdUpdateGBImage* cmd = (SVGA3dCmdUpdateGBImage*)ReserveFIFO3D((uint)FIFOCommand.SVGA_3D_CMD_UPDATE_GB_IMAGE, (uint)sizeof(SVGA3dCmdUpdateGBImage));
            cmd->image = surface.SurfaceID;
            cmd->box.x = 0; cmd->box.y = 0; cmd->box.z = 0;
            cmd->box.w = width; cmd->box.h = height; cmd->box.d = 1;

            SyncToFence(InsertFence());
        }
        else
        {
            void* buffer = SVGA3DUtil_AllocDMABuffer(width * height * sizeof(int), out SVGAGuestPtr gPtr);
            fixed (int* rawDataPtr = image)
            {
                MemoryOp.MemCopy((byte*)buffer, (byte*)rawDataPtr, image.Length * sizeof(int));
            }
            SurfaceDMA2D(surface.SurfaceID.sid, &gPtr, SVGA3dTransferType.SVGA3D_WRITE_HOST_VRAM, width, height);
            SyncToFence(InsertFence());
            PopDMABuffer();
        }

        return surface;
    }

    public uint CreateStaticArrayBuffer<T>(T[] data) where T : unmanaged
    {
        uint size = (uint)(data.Length * sizeof(T));
        var surface = DefineSurface(size, 1, 1, SVGA3dSurfaceFormat.SVGA3D_BUFFER, 1);

        if (CheckGBCached())
        {
            fixed (T* pData = &data[0])
            {
                MemoryOp.MemCopy((byte*)surface.MobPtr, (byte*)pData, (int)size);
            }

            SVGA3dCmdUpdateGBImage* cmd = (SVGA3dCmdUpdateGBImage*)ReserveFIFO3D((uint)FIFOCommand.SVGA_3D_CMD_UPDATE_GB_IMAGE, (uint)sizeof(SVGA3dCmdUpdateGBImage));
            cmd->image = surface.SurfaceID;
            cmd->box.x = 0; cmd->box.y = 0; cmd->box.z = 0;
            cmd->box.w = size; cmd->box.h = 1; cmd->box.d = 1;

            SyncToFence(InsertFence());
        }
        else
        {
            void* buffer = SVGA3DUtil_AllocDMABuffer(size, out SVGAGuestPtr gPtr);
            fixed (T* pData = &data[0])
            {
                MemoryOp.MemCopy((byte*)buffer, (byte*)pData, (int)size);
            }
            SurfaceDMA2D(surface.SurfaceID.sid, &gPtr, SVGA3dTransferType.SVGA3D_WRITE_HOST_VRAM, size, 1);
            SyncToFence(InsertFence());
            PopDMABuffer();
        }

        return surface.SurfaceID.sid;
    }

    public uint TestDebugBuffer()
    {
        var surface = DefineSurface(1280, 720, 1, SVGA3dSurfaceFormat.SVGA3D_A8R8G8B8);
        
        if (CheckGBCached())
        {
            MemoryOp.MemSet((byte*)surface.MobPtr, 0x30, 1280 * 720 * 4);

            SVGA3dCmdUpdateGBImage* cmd = (SVGA3dCmdUpdateGBImage*)ReserveFIFO3D((uint)FIFOCommand.SVGA_3D_CMD_UPDATE_GB_IMAGE, (uint)sizeof(SVGA3dCmdUpdateGBImage));
            cmd->image = surface.SurfaceID;
            cmd->box.x = 0; cmd->box.y = 0; cmd->box.z = 0;
            cmd->box.w = 1280; cmd->box.h = 720; cmd->box.d = 1;

            SyncToFence(InsertFence());
        }
        else
        {
            void* buffer = SVGA3DUtil_AllocDMABuffer(1280 * 720 * 4, out SVGAGuestPtr gPtr);
            MemoryOp.MemSet((byte*)buffer, 0x30, 1280 * 720 * 4);

            SurfaceDMA2D(surface.SurfaceID.sid, &gPtr, SVGA3dTransferType.SVGA3D_WRITE_HOST_VRAM, 1280, 720);
            SyncToFence(InsertFence());
            PopDMABuffer();
        }

        return surface.SurfaceID.sid;
    }

    private void BeginPresent(uint sid, SVGA3dCopyRect** rects, uint numRects)
    {
        SVGA3dCmdPresent* cmd = (SVGA3dCmdPresent*)ReserveFIFO3D((uint)FIFOCommand.PRESENT, (uint)sizeof(SVGA3dCmdPresent) + (uint)(numRects * sizeof(SVGA3dCopyRect)));
        cmd->sid = sid;
        *rects = (SVGA3dCopyRect*)&cmd[1];
    }

    public void Present(SVGA3dSurfaceImageId image, SVGA3dRect PresentRect)
    {
        SVGA3dCopyRect* rect;

        SyncToFence(_lastFence);

        BeginPresent(image.sid, &rect, 1);
        MemoryOp.MemSet((byte*)rect, 0, sizeof(SVGA3dCopyRect));
        rect->x = PresentRect.x;
        rect->y = PresentRect.y;
        rect->w = PresentRect.w;
        rect->h = PresentRect.h;

        _lastFence = InsertFence();
    }

    public int[]? PresentToImage(GBSurface surface, SVGA3dRect rect, bool inverted = false)
    {
        uint width = rect.w;
        uint height = rect.h;

        if (width == 0 || height == 0)
        {
            return null;
        }

        int pixelCount = (int)(width * height);
        if (_imagebuffer.Length != pixelCount)
        {
            _imagebuffer = new int[pixelCount];
        }

        if (CheckGBCached())
        {
            SVGA3dCmdReadbackGBImagePartial* cmd = (SVGA3dCmdReadbackGBImagePartial*)ReserveFIFO3D((uint)FIFOCommand.SVGA_3D_CMD_READBACK_GB_IMAGE, (uint)sizeof(SVGA3dCmdReadbackGBImagePartial));
            cmd->image = surface.SurfaceID;
            cmd->box.x = rect.x; cmd->box.y = rect.y; cmd->box.z = 0;
            cmd->box.w = width; cmd->box.h = height; cmd->box.d = 1;
            cmd->invertBox = inverted ? 1u : 0u;

            SyncToFence(InsertFence());

            fixed (int* pDest = &_imagebuffer[0])
            {
                MemoryOp.MemCopy((byte*)pDest, (byte*)surface.MobPtr, pixelCount * 4);
            }
        }
        else
        {
            void* buffer = SVGA3DUtil_AllocDMABuffer(width * height * 4, out SVGAGuestPtr gPtr);
            
            SVGA3dGuestImage guestImage;
            guestImage.ptr = gPtr;
            guestImage.pitch = 0;
            
            SVGA3dSurfaceImageId hostImage = surface.SurfaceID;

            SVGA3dCopyBox* boxes;
            BeginSurfaceDMA(&guestImage, &hostImage, SVGA3dTransferType.SVGA3D_READ_HOST_VRAM, &boxes, 1);

            boxes[0].x = rect.x;
            boxes[0].y = rect.y;
            boxes[0].w = width;
            boxes[0].h = height;
            boxes[0].d = 1;

            SyncToFence(InsertFence());

            fixed (int* pDest = &_imagebuffer[0])
            {
                MemoryOp.MemCopy((byte*)pDest, (byte*)buffer, pixelCount * 4);
            }

            PopDMABuffer();
        }

        return _imagebuffer;
    }

    #endregion
    #region Internal DMA & MOB Utilities

    private void BeginSurfaceDMA(
        SVGA3dGuestImage* guestImage,
        SVGA3dSurfaceImageId* hostImage,
        SVGA3dTransferType transfer,
        SVGA3dCopyBox** boxes,
        uint numBoxes)
    {
        SVGA3dCmdSurfaceDMA* cmd;
        uint boxesSize = (uint)sizeof(SVGA3dCopyBox) * numBoxes;

        cmd = (SVGA3dCmdSurfaceDMA*)ReserveFIFO3D((uint)FIFOCommand.SURFACE_DMA, (uint)sizeof(SVGA3dCmdSurfaceDMA) + boxesSize);

        cmd->guest = *guestImage;
        cmd->host = *hostImage;
        cmd->transfer = transfer;
        *boxes = (SVGA3dCopyBox*)&cmd[1];

        MemoryOp.MemSet((byte*)*boxes, 0, (int)boxesSize);
    }

    private void SurfaceDMA2D(
        uint sid,
        SVGAGuestPtr* guestPtr,
        SVGA3dTransferType transfer,
        uint width,
        uint height)
    {
        SVGA3dCopyBox* boxes;
        SVGA3dGuestImage guestImage;
        SVGA3dSurfaceImageId hostImage = new() { sid = sid };

        guestImage.ptr = *guestPtr;
        guestImage.pitch = 0;

        BeginSurfaceDMA(&guestImage, &hostImage, transfer, &boxes, 1);
        boxes[0].w = width;
        boxes[0].h = height;
        boxes[0].d = 1;
    }

    private static SVGAGuestPtr s_nextPtr = new SVGAGuestPtr { gmrId = 0, offset = 0 };
    private static SVGAGuestPtr s_dmaStart = new SVGAGuestPtr { gmrId = 0, offset = 0 };
    private static uint s_dmaSize = 0;
    private const uint SVGA_GMR_FRAMEBUFFER = 0xFFFFFFFEu;

    public void PopDMABuffer()
    {
        if (_lastDMASize <= (s_nextPtr.offset - s_dmaStart.offset))
        {
            s_nextPtr.offset -= _lastDMASize;
        }
        _lastDMASize = 0;
    }

    public void* SVGA3DUtil_AllocDMABuffer(uint size, out SVGAGuestPtr ptr)
    {
        uint alignedSize = (size + 3u) & ~3u;
        if ((_driver.Capabilities & (uint)Capability.Gmr) == 0)
        {
            throw new InvalidOperationException("SVGA device does not support GMR — cannot allocate framebuffer-backed guest pointer.");
        }

        if (s_nextPtr.offset + alignedSize > _driver.VideoMemory.Size)
        {
            throw new OutOfMemoryException(
                $"DMA scratch buffer request of {alignedSize} bytes exceeds remaining scratch space " +
                $"({_driver.VideoMemory.Size - s_nextPtr.offset} bytes free, region size {s_dmaSize} bytes). " +
                $"Consider splitting the upload into smaller chunks."
            );
        }

        ptr = new SVGAGuestPtr
        {
            gmrId = SVGA_GMR_FRAMEBUFFER,
            offset = s_nextPtr.offset
        };

        void* buffer = (void*)(_driver.VideoMemory.Base + s_nextPtr.offset);

        s_nextPtr.offset += alignedSize;
        _lastDMASize = alignedSize;

        return buffer;
    }

    public uint DefineGBMob(uint sizeInBytes, out void* buffer, out SVGAGuestPtr gPtr)
    {
        uint mobid = GetNextMobId();

        uint alignedSize = (sizeInBytes + 4095u) & ~4095u;
        gPtr = new SVGAGuestPtr { gmrId = 0xFFFFFFFEu, offset = _persistentMobOffset };
        buffer = (void*)(_driver.VideoMemory.Base + _persistentMobOffset);
        
        _persistentMobOffset += alignedSize;

        uint mobBasePPN = ((uint)_driver.VideoMemory.Base + gPtr.offset) / 4096u;

        MobFormat ptDepth;
        uint basePPN;

        if (sizeInBytes <= 4096)
        {
            ptDepth = MobFormat.PTDEPTH_0;
            basePPN = mobBasePPN;
        }
        else
        {
            ptDepth = MobFormat.PTDEPTH_1;
            
            uint numPages = alignedSize / 4096u;
            uint pageTableSize = numPages * sizeof(uint);
            
            uint* pageTable = (uint*)(_driver.VideoMemory.Base + _persistentMobOffset);
            _persistentMobOffset += (pageTableSize + 4095u) & ~4095u;
            
            for (uint i = 0; i < numPages; i++)
            {
                pageTable[i] = mobBasePPN + i;
            }

            uint ptPhysicalAddress = (uint)pageTable;
            basePPN = ptPhysicalAddress / 4096u;
        }

        if (_persistentMobOffset >= s_dmaStart.offset)
        {
            throw new OutOfMemoryException("Persistent MOB memory collided with the DMA scratch buffer.");
        }

        SVGA3dCmdDefineGBMob* cmd = (SVGA3dCmdDefineGBMob*)ReserveFIFO3D(
            (uint)FIFOCommand.SVGA_3D_CMD_DEFINE_GB_MOB, (uint)sizeof(SVGA3dCmdDefineGBMob)
        );

        cmd->mobid = mobid;
        cmd->ptDepth = ptDepth;
        cmd->basePPN = basePPN;
        cmd->sizeInBytes = sizeInBytes;

        return mobid;
    }

    public void DestroyGBMob(uint mobid)
    {
        SVGA3dCmdDestroyGBMob* cmd = (SVGA3dCmdDestroyGBMob*)ReserveFIFO3D(
            (uint)FIFOCommand.SVGA_3D_CMD_DESTROY_GB_MOB, (uint)sizeof(SVGA3dCmdDestroyGBMob)
        );
            
        cmd->mobid = mobid;
    }

    bool? _gbcache = null;
    bool CheckGBCached()
    {
        if (!_gbcache.HasValue)
        {
            _gbcache = (_driver.Capabilities & (uint)Capability.GuestBackedObjects) != 0;
        }

        return _gbcache.Value;
    }

    public static uint GetBytesPerPixel(SVGA3dSurfaceFormat format)
    {
        return format switch
        {
            SVGA3dSurfaceFormat.SVGA3D_A8R8G8B8 => 4,
            SVGA3dSurfaceFormat.SVGA3D_X8R8G8B8 => 4,
            SVGA3dSurfaceFormat.SVGA3D_BUFFER   => 1,
            _ => 4
        };
    }

    public static uint CalculateSurfaceMobSize(uint width, uint height, uint depth, uint mipLevels, SVGA3dSurfaceFormat format)
    {
        uint bpp = GetBytesPerPixel(format);
        uint totalBytes = 0;

        uint w = width;
        uint h = height;
        uint d = depth;

        for (int i = 0; i < mipLevels; i++)
        {
            totalBytes += w * h * d * bpp;

            w = Math.Max(1u, w / 2u);
            h = Math.Max(1u, h / 2u);
            d = Math.Max(1u, d / 2u);
        }

        return (totalBytes + 4095u) & ~4095u;
    }

    #endregion
}