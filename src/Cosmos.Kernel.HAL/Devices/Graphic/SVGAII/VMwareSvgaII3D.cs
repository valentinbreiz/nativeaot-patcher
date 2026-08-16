using System;
using System.Numerics;
using Cosmos.Kernel.Core.IO;
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

        _fifoFenceSupported = CheckGBCached() && driver.Is3DEnabled;

        s_dmaSize = _driver.VideoMemory.Size / 8;
        uint dmaStartOffset = (_driver.VideoMemory.Size - s_dmaSize) & ~3u;

        s_dmaStart = new SVGAGuestPtr { gmrId = SVGA_GMR_FRAMEBUFFER, offset = dmaStartOffset };
        s_nextPtr.offset = s_dmaStart.offset;

        if (CheckGBCached())
        {
            MobAllocator.SetupOTables(this,_driver);
        }
    }

    private uint _contextId = 1;
    private GBContext? _boundContextId = null;

    private uint GetNextContextId() => ++_contextId;

    private uint CurrentContextId => _boundContextId?.ContextID 
        ?? throw new InvalidOperationException("No context is currently bound! Call BindContext first.");

    private uint _surfaceId = 1;
    private GBSurface? _boundSurfaceId = null;

    private uint GetNextSurfaceId() => ++_surfaceId;

    private uint _viewId = 1;
    private uint GetNextViewId() => ++_viewId;

    private uint _persistentMobOffset = 0;

    private bool _fifoFenceSupported;
    private uint _guestFenceCounter = 1;

    private uint _lastDMASize = 0;

    private int[] _imagebuffer = [];
    private uint _lastFence = 1;

    private uint _shaderIdVS = 0;
    private uint _shaderIdPS = 0;

    private GBDepthStencilView _gBDepthStencilViewTarget = GBDepthStencilView.Unbound;
    private List<GBColorView> _gBColorViewTargets = new();

    private static readonly (uint MaxEntries, uint EntrySize)[] s_coInfo =
    [
        (1, (uint)sizeof(SVGACOTableDXRTViewEntry)),
        (1, (uint)sizeof(SVGACOTableDXDSViewEntry)),
        (1, (uint)sizeof(SVGACOTableDXRTViewEntry)),
        (4096 / (uint)sizeof(SVGACOTableDXElementLayoutEntry) + 1, (uint)sizeof(SVGACOTableDXElementLayoutEntry)),
        (4096 / (uint)sizeof(SVGACOTableDXBlendStateEntry) + 1, (uint)sizeof(SVGACOTableDXBlendStateEntry)),
        (1, (uint)sizeof(SVGACOTableDXDepthStencilEntry)),
        (1, (uint)sizeof(SVGACOTableDXRasterizerStateEntry)),
        (1, (uint)sizeof(SVGACOTableDXSamplerEntry)),
        (1, (uint)sizeof(SVGACOTableDXStreamOutputEntry)),
        (1, (uint)sizeof(SVGACOTableDXQueryEntry)),
        (1, (uint)sizeof(SVGACOTableDXShaderEntry)),
        (1, (uint)sizeof(SVGACOTableDXUAViewEntry))
    ];

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
        uint cmdid = (uint)(
            CheckDXCached() ? FIFOCommand.SVGA_3D_CMD_DX_DEFINE_CONTEXT :
            CheckGBCached() ? FIFOCommand.SVGA_3D_CMD_DEFINE_GB_CONTEXT :
            FIFOCommand.DEFINE_CONTEXT
        );
        Serial.WriteString("[DefineContext] cid="); Serial.WriteHex(cid);
        Serial.WriteString(" cmdid="); Serial.WriteHex(cmdid);
        Serial.WriteString(" dxCached="); Serial.WriteHex(CheckDXCached() ? 1u : 0u);
        Serial.WriteString(" gbCached="); Serial.WriteHex(CheckGBCached() ? 1u : 0u);
        Serial.WriteString("\n");

        void* mobPtr = (void*)0;
        var mobid = CheckModernCached() ? MobAllocator.DefineGBMob(
            this, _driver,
            CheckDXCached() ? (uint)sizeof(SVGADXContextMobFormat) : (uint)sizeof(SVGAGBContextData),
            out mobPtr, out _
        ) : 0;
        Serial.WriteString("[DefineContext] mobid="); Serial.WriteHex(mobid);
        Serial.WriteString(" mobPtr=0x"); Serial.WriteHex((ulong)mobPtr);
        Serial.WriteString(" contextMobSize="); Serial.WriteHex((uint)sizeof(SVGADXContextMobFormat));
        Serial.WriteString("\n");

        uint nextBefore = _driver.GetFIFO(FIFO.NextCmd);
        SVGA3dCmdDefineContext* cmd = (SVGA3dCmdDefineContext*)ReserveFIFO3D(cmdid, (uint)sizeof(SVGA3dCmdDefineContext));
        cmd->cid = cid;
        _driver.CommitFIFOCommand();
        uint stopAfter = _driver.GetFIFO(FIFO.Stop);
        Serial.WriteString("[DefineContext] cmd nextBefore="); Serial.WriteHex(nextBefore);
        Serial.WriteString(" stopAfter="); Serial.WriteHex(stopAfter);
        Serial.WriteString(" delta="); Serial.WriteHex(stopAfter - nextBefore);
        Serial.WriteString(" expected="); Serial.WriteHex((uint)sizeof(SVGA3dCmdHeader) + (uint)sizeof(SVGA3dCmdDefineContext));
        Serial.WriteString("\n");

        var ctx = new GBContext { ContextID = cid, MobID = mobid, MobPtr = mobPtr, CoTables = new() };
        _driver.WaitForFifo();
        Serial.WriteString("[DefineContext] released from object wait \n");

        if (CheckDXCached())
        {
            uint cotmax = (uint)(CheckSM5Cached() ? SVGACOTableType.SVGA_COTABLE_MAX : SVGACOTableType.SVGA_COTABLE_DX10_MAX);
            Serial.WriteString("[DefineContext] cotmax="); Serial.WriteHex(cotmax); Serial.WriteString("\n");

            for (uint i = 0; i < cotmax; i++)
            {
                var entry = s_coInfo[(int)i];
                var size = entry.MaxEntries * entry.EntrySize;
                uint comob = MobAllocator.DefineGBMob(this, _driver, size, out void* coPtr, out _);
                Serial.WriteString("[DefineContext] cotable i="); Serial.WriteHex(i);
                Serial.WriteString(" size="); Serial.WriteHex(size);
                Serial.WriteString(" mobid="); Serial.WriteHex(comob);
                Serial.WriteString("\n");

                if (i == (uint)SVGACOTableType.SVGA_COTABLE_RTVIEW)
                {
                    _rtviewCOTableMobPtr = coPtr;
                }
                ctx.CoTables.Add(new() { Type = (SVGACOTableType)i, MobID = comob, Size = size, DataPtr = coPtr });
            }
        }
        else
        {
            Serial.WriteString($"[DefineContext] skipped DX check {CheckDXCached()} \n");
        }

        InternalBindContext(ctx,true);
        return ctx;
    }

    private void* _rtviewCOTableMobPtr;

    private void BindCOTable(uint ctx, CoTable table)
    {
        var cmd = (SVGA3dCmdDXSetCOTable*)ReserveFIFO3D(
            (uint)FIFOCommand.SVGA_3D_CMD_DX_SET_COTABLE,
            (uint)sizeof(SVGA3dCmdDXSetCOTable)
        );

        cmd->cid = ctx;
        cmd->mobid = table.MobID;
        cmd->type = table.Type;
        cmd->validSizeInBytes = 0;

        uint nextBefore = _driver.GetFIFO(FIFO.NextCmd);
        _driver.CommitFIFOCommand();
        uint stopAfter = _driver.GetFIFO(FIFO.Stop);

        Serial.WriteString("[BindCOTable] cid="); Serial.WriteHex(ctx);
        Serial.WriteString(" type="); Serial.WriteHex((uint)table.Type);
        Serial.WriteString(" mobid="); Serial.WriteHex(table.MobID);
        Serial.WriteString(" nextBefore="); Serial.WriteHex(nextBefore);
        Serial.WriteString(" stopAfter="); Serial.WriteHex(stopAfter);
        Serial.WriteString(" delta="); Serial.WriteHex(stopAfter - nextBefore);
        Serial.WriteString(" expected="); Serial.WriteHex((uint)sizeof(SVGA3dCmdHeader) + (uint)sizeof(SVGA3dCmdDXSetCOTable));
        Serial.WriteString("\n");
    }

    private void GrowCOTable(uint cid, SVGACOTableType type, uint mobid, uint sizeInBytes)
    {
        var cmd = (SVGA3dCmdDXSetCOTable*)ReserveFIFO3D(
            (uint)FIFOCommand.SVGA_3D_CMD_DX_GROW_COTABLE,
            (uint)sizeof(SVGA3dCmdDXSetCOTable)
        );

        cmd->cid = cid;
        cmd->mobid = mobid;
        cmd->type = type;
        cmd->validSizeInBytes = sizeInBytes;

        _driver.CommitFIFOCommand();
        SyncToFence(InsertFence());
    }

    public void DebugContextMob(GBContext ctx)
    {
        var dxctx = (SVGADXContextMobFormat*)ctx.MobPtr;
        Console.WriteLine($"ctx mob: {dxctx->depthStencilViewId:X8} {dxctx->renderTargetViewIds[0]:X8} ");
        
        Console.WriteLine($"viewports: {dxctx->numViewports:X8} ");
        var vp0 = (SVGA3dViewport*)&dxctx->viewportsRaw[0];
        Console.WriteLine($"viewport 0: {vp0->x} {vp0->y} {vp0->w} {vp0->h} {vp0->dmin} {vp0->dmax} ");
    }
    public void DebugCOTables()
    {
        byte* p = (byte*)_rtviewCOTableMobPtr;
        for (int i = 0; i < 64; i++)
        {
            Console.Write(p[i].ToString("X2") + " ");
        }
    }

    public void ReadbackContext(GBContext ctx)
    {
        var cmd = (uint*)ReserveFIFO3D(
            (uint)FIFOCommand.SVGA_3D_CMD_DX_READBACK_CONTEXT,
            sizeof(uint)
        );

        *cmd = ctx.ContextID;

        var nextcmd = _driver.GetFIFO(FIFO.NextCmd);

        _driver.CommitFIFOCommand();
        SyncToFence(InsertFence());

        var ncmd = _driver.GetFIFO(FIFO.NextCmd);

        var stopvalue = _driver.GetFIFO(FIFO.Stop);

        if (stopvalue != ncmd)
        {
            Console.WriteLine($"Values are diffrent {nextcmd} {ncmd} {stopvalue}");
        }
        else
        {
            Console.WriteLine($"Values are good {nextcmd} {ncmd} {stopvalue}");
        }
    }

    public GBContext BindContext(GBContext context) => InternalBindContext(context);

    GBContext InternalBindContext(GBContext context,bool invalidate = false)
    {
        if (_boundContextId.HasValue && _boundContextId.Value.ContextID == context.ContextID)
        {
            return _boundContextId.Value;
        }

        if (CheckModernCached())
        {
            uint cmdid = (uint)(
                CheckDXCached() ? FIFOCommand.SVGA_3D_CMD_DX_BIND_CONTEXT :
                FIFOCommand.SVGA_3D_CMD_BIND_GB_CONTEXT
            );

            SVGA3dCmdBindGBContext* cmd;
            cmd = (SVGA3dCmdBindGBContext*)ReserveFIFO3D(cmdid, (uint)sizeof(SVGA3dCmdBindGBContext));
            cmd->cid = context.ContextID;
            cmd->mobid = context.MobID;
            cmd->validContents = invalidate ? 0u : 1u;

            uint nextBefore = _driver.GetFIFO(FIFO.NextCmd);
            _driver.CommitFIFOCommand();
            uint stopAfter = _driver.GetFIFO(FIFO.Stop);
            Serial.WriteString("[BindContext] cid="); Serial.WriteHex(context.ContextID);
            Serial.WriteString(" mobid="); Serial.WriteHex(context.MobID);
            Serial.WriteString(" validContents="); Serial.WriteHex(cmd->validContents);
            Serial.WriteString(" nextBefore="); Serial.WriteHex(nextBefore);
            Serial.WriteString(" stopAfter="); Serial.WriteHex(stopAfter);
            Serial.WriteString(" delta="); Serial.WriteHex(stopAfter - nextBefore);
            Serial.WriteString(" expected="); Serial.WriteHex((uint)sizeof(SVGA3dCmdHeader) + (uint)sizeof(SVGA3dCmdBindGBContext));
            Serial.WriteString(" cotables="); Serial.WriteNumber(context.CoTables.Count);
            Serial.WriteString("\n");

            foreach (var item in context.CoTables)
            {
                BindCOTable(context.ContextID,item);
            }
        }

        _boundContextId = context;
        return _boundContextId.Value;
    }

    public void DestroyContext(GBContext context)
    {
        uint cmdid = (uint)(
            CheckDXCached() ? FIFOCommand.SVGA_3D_CMD_DX_DESTROY_CONTEXT :
            CheckGBCached() ? FIFOCommand.SVGA_3D_CMD_DESTROY_GB_CONTEXT :
            FIFOCommand.DESTROY_CONTEXT
        );

        uint* cmd = (uint*)ReserveFIFO3D(cmdid, sizeof(uint));
        *cmd = context.ContextID;

        _driver.CommitFIFOCommand();

        if (CheckModernCached())
        {
            foreach (var item in context.CoTables)
            {
                MobAllocator.DestroyGBMob(this,_driver,item.MobID);
            }

            MobAllocator.DestroyGBMob(this,_driver,context.MobID);
        }
    }

    #endregion
    #region Surface Management

    public GBSurface DefineSurface(uint width, uint height, uint depth, SVGA3dSurfaceFormat format,SVGA3dSurfaceFlags flags = SVGA3dSurfaceFlags.SVGA3D_SURFACE_HINT_TEXTURE, uint mips = 1, uint arraySize = 1)
    {
        uint sid = GetNextSurfaceId();

        if (CheckGBCached())
        {
            uint mobSize = CalculateSurfaceMobSize(width, height, depth, mips, format);
            uint mobid = MobAllocator.DefineGBMob(this,_driver,mobSize, out void* mobPtr, out _);

            SVGA3dCmdDefineGBSurface* cmd = (SVGA3dCmdDefineGBSurface*)ReserveFIFO3D(
                (uint)FIFOCommand.SVGA_3D_CMD_DEFINE_GB_SURFACE_V2,
                (uint)sizeof(SVGA3dCmdDefineGBSurface)
            );

            cmd->sid = sid;
            cmd->flags = flags;
            cmd->format = format;
            cmd->numMipLevels = mips;
            cmd->multisampleCount = 0;
            cmd->autogenFilter = 0;
            cmd->size.width = width;
            cmd->size.height = height;
            cmd->size.depth = depth;
            cmd->arraySize = arraySize;

            _driver.CommitFIFOCommand();

            var surface = new GBSurface
            {
                SurfaceID = new() { sid = sid, face = 0, mipmap = 0 },
                MobID = mobid,
                MobPtr = mobPtr,
                Flags = flags,
                Resolution = new(0,0,0,width,height,depth),
                Format = format
            };

            SyncToFence(InsertFence());

            BindSurface(surface);

            return surface;
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

            _driver.CommitFIFOCommand();

            return new GBSurface
            {
                SurfaceID = new() { sid = sid, face = 0, mipmap = 0 },
                MobID = 0,
                MobPtr = null,
                Flags = flags,
                Resolution = new(0,0,0,width,height,depth),
                Format = format
            };
        }
    }

    public GBSurface BindSurface(GBSurface surface)
    {
        if (_boundSurfaceId.HasValue && _boundSurfaceId.Value.SurfaceID.sid == surface.SurfaceID.sid)
        {
            return _boundSurfaceId.Value;
        }

        if (CheckGBCached())
        {
            SVGA3dCmdBindGBSurface* cmd = (SVGA3dCmdBindGBSurface*)ReserveFIFO3D((uint)FIFOCommand.SVGA_3D_CMD_BIND_GB_SURFACE, (uint)sizeof(SVGA3dCmdBindGBSurface));
            cmd->sid = surface.SurfaceID.sid;
            cmd->mobid = surface.MobID;

            _driver.CommitFIFOCommand();
        }

        _boundSurfaceId = surface;
        return _boundSurfaceId.Value;
    }

    public void DestroySurface(GBSurface surface)
    {
        uint* cmd = (uint*)ReserveFIFO3D((uint)(CheckGBCached() ? FIFOCommand.SVGA_3D_CMD_DESTROY_GB_SURFACE : FIFOCommand.DESTROY_SURFACE), sizeof(uint));
        *cmd = surface.SurfaceID.sid;

        _driver.CommitFIFOCommand();

        if (surface.MobID != 0)
        {
            MobAllocator.DestroyGBMob(this,_driver,surface.MobID);
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
            uint mobid = MobAllocator.DefineGBMob(this,_driver,size, out void* mobPtr, out _);

            fixed (byte* bytecodePtr = bytecode)
            {
                MemoryOp.MemCopy((byte*)mobPtr, bytecodePtr, bytecode.Length);
            }

            SVGA3dCmdDefineGBShader* cmd = (SVGA3dCmdDefineGBShader*)ReserveFIFO3D((uint)FIFOCommand.SVGA_3D_CMD_DEFINE_GB_SHADER, (uint)sizeof(SVGA3dCmdDefineGBShader));
            cmd->shid = shid;
            cmd->type = type;
            cmd->sizeInBytes = size;

            _driver.CommitFIFOCommand();

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

            _driver.CommitFIFOCommand();

            return new GBShader { ShaderID = shid, MobID = 0, Type = type, MobPtr = null };
        }
    }

    void BindShaderInt(GBShader shader)
    {
        if (CheckGBCached())
        {
            SVGA3dCmdBindGBShader* cmd = (SVGA3dCmdBindGBShader*)ReserveFIFO3D((uint)FIFOCommand.SVGA_3D_CMD_BIND_GB_SHADER, (uint)sizeof(SVGA3dCmdBindGBShader));
            cmd->shid = shader.ShaderID;
            cmd->mobid = shader.MobID;
            cmd->offsetInBytes = 0;

            _driver.CommitFIFOCommand();
        }
    }

    public void BindShader(GBShader shader)
    {
        BindShaderInt(shader);

        SVGA3dCmdSetShader* cmd = (SVGA3dCmdSetShader*)ReserveFIFO3D((uint)FIFOCommand.SET_SHADER, (uint)sizeof(SVGA3dCmdSetShader));
        cmd->cid = CurrentContextId;
        cmd->type = shader.Type;
        cmd->shid = shader.ShaderID;

        _driver.CommitFIFOCommand();
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

        _driver.CommitFIFOCommand();
    }

    public void DestroyShader(GBShader shader)
    {
        if (CheckGBCached())
        {
            SVGA3dCmdDestroyGBShader* cmd = (SVGA3dCmdDestroyGBShader*)ReserveFIFO3D((uint)FIFOCommand.SVGA_3D_CMD_DESTROY_GB_SHADER, (uint)sizeof(SVGA3dCmdDestroyGBShader));
            cmd->shid = shader.ShaderID;

            _driver.CommitFIFOCommand();

            MobAllocator.DestroyGBMob(this,_driver,shader.MobID);
        }
        else
        {
            SVGA3dCmdDestroyShader* cmd = (SVGA3dCmdDestroyShader*)ReserveFIFO3D((uint)FIFOCommand.DESTROY_SHADER, (uint)sizeof(SVGA3dCmdDestroyShader));
            cmd->cid = CurrentContextId;
            cmd->shid = shader.ShaderID;
            cmd->type = shader.Type;

            _driver.CommitFIFOCommand();
        }
    }

    #endregion
    #region  State & Render Commands

    public GBColorView CreateColorView(GBSurface color,uint viewDimensions = 2,uint mipSlice = 0,uint firstArraySlice = 0,uint arraySize = 1)
    {
        if (!color.Flags.HasFlag(SVGA3dSurfaceFlags.SVGA3D_SURFACE_HINT_RENDERTARGET))
        {
            throw new ArgumentException("Surface must have SVGA3D_SURFACE_HINT_RENDERTARGET to back a render target view.");
        }

        uint vid = GetNextViewId();

        if (CheckDXCached())
        {
            SVGA3dCmdDXDefineRenderTargetView* cmd = (SVGA3dCmdDXDefineRenderTargetView*)ReserveFIFO3D(
                (uint)FIFOCommand.SVGA_3D_CMD_DX_DEFINE_RENDERTARGET_VIEW, 
                (uint)sizeof(SVGA3dCmdDXDefineRenderTargetView)
            );

            cmd->renderTargetViewId = vid;
            cmd->sid = color.SurfaceID.sid;
            cmd->format = color.Format;
            cmd->resourceDimension = viewDimensions + 1;
            cmd->desc.tex.mipSlice = mipSlice;
            cmd->desc.tex.firstArraySlice = firstArraySlice;
            cmd->desc.tex.arraySize = arraySize;

            _driver.CommitFIFOCommand();
            SyncToFence(InsertFence());
        }

        return new()
        {
            Surface = color,
            ViewID = vid
        };
    }

    public GBDepthStencilView CreateDepthstencilView(GBSurface surface, bool useDepth = true,bool useStencil = false,uint viewDimensions = 2,uint mipSlice = 0,uint firstArraySlice = 0,uint arraySize = 1)
    {
        if (!surface.Flags.HasFlag(SVGA3dSurfaceFlags.SVGA3D_SURFACE_HINT_DEPTHSTENCIL))
        {
            throw new ArgumentException("Surface must have SVGA3D_SURFACE_HINT_DEPTHSTENCIL to back a render target view.");
        }

        uint vid = GetNextViewId();

        if (CheckDXCached())
        {
            SVGA3dCmdDXDefineDepthStencilView* cmd = (SVGA3dCmdDXDefineDepthStencilView*)ReserveFIFO3D(
                (uint)FIFOCommand.SVGA_3D_CMD_DX_DEFINE_DEPTHSTENCIL_VIEW, 
                (uint)sizeof(SVGA3dCmdDXDefineDepthStencilView)
            );

            cmd->depthStencilViewId = vid;
            cmd->sid = surface.SurfaceID.sid;
            cmd->format = surface.Format;
            cmd->resourceDimension = viewDimensions + 1;
            cmd->mipSlice = mipSlice;
            cmd->firstArraySlice = firstArraySlice;
            cmd->arraySize = arraySize;
            cmd->flags = 0;

            _driver.CommitFIFOCommand();
            SyncToFence(InsertFence());
        }

        return new()
        {
            DepthPresent = useDepth,
            StencilPresent = useStencil,
            Surface = surface,
            ViewID = vid
        };
    }

    public void DestroyView(GBColorView view)
    {
        if (CheckDXCached())
        {
            uint* cmd = (uint*)ReserveFIFO3D((uint)FIFOCommand.SVGA_3D_CMD_DX_DESTROY_RENDERTARGET_VIEW, sizeof(uint));
            *cmd = view.ViewID;

            _driver.CommitFIFOCommand();
        }
    }

    public void DestroyView(GBDepthStencilView view)
    {
        if (CheckDXCached())
        {
            uint* cmd = (uint*)ReserveFIFO3D((uint)FIFOCommand.SVGA_3D_CMD_DX_DESTROY_DEPTHSTENCIL_VIEW, sizeof(uint));
            *cmd = view.ViewID;
            
            _driver.CommitFIFOCommand();
        }
    }

    public void SetRenderTargets(GBDepthStencilView depthstencil, params GBColorView[] color)
    {
        if (depthstencil.DepthPresent && !depthstencil.Surface.Flags.HasFlag(SVGA3dSurfaceFlags.SVGA3D_SURFACE_HINT_DEPTHSTENCIL))
        {
            throw new ArgumentException($"Tried to bind an invalid surface to Depth/Stencil. The SVGA3D_SURFACE_HINT_DEPTHSTENCIL flag is required for depth/stencil targets.");
        }

        _gBColorViewTargets.Clear();
        _gBDepthStencilViewTarget = depthstencil;

        foreach (var target in color)
        {
            if (!target.Surface.Flags.HasFlag(SVGA3dSurfaceFlags.SVGA3D_SURFACE_HINT_RENDERTARGET))
            {
                throw new ArgumentException($"Tried to bind an invalid surface to Color. The SVGA3D_SURFACE_HINT_RENDERTARGET flag is required for color targets.");
            }

            _gBColorViewTargets.Add(target);
        }

        if (CheckDXCached())
        {
            uint* cmd = (uint*)ReserveFIFO3D((uint)FIFOCommand.SVGA_3D_CMD_DX_SET_RENDERTARGETS, (uint)((color.Length + 1) * sizeof(uint)));
            *cmd = depthstencil.ViewID;

            for (int i = 0; i < color.Length; i++)
            {
                *(cmd + i + 1) = color[i].ViewID;
            }

            _driver.CommitFIFOCommand();
        }
        else
        {
            for (int i = 0; i < color.Length; i++)
            {
                SVGA3dCmdSetRenderTarget* cmd = (SVGA3dCmdSetRenderTarget*)ReserveFIFO3D((uint)FIFOCommand.SET_RENDER_TARGET, (uint)sizeof(SVGA3dCmdSetRenderTarget));
                cmd->cid = CurrentContextId;
                cmd->type = (SVGA3dRenderTargetType)((uint)SVGA3dRenderTargetType.Color0 + i);
                cmd->target = color[i].Surface.SurfaceID;

                _driver.CommitFIFOCommand();
            }

            if (depthstencil.DepthPresent)
            {
                SVGA3dCmdSetRenderTarget* cmd = (SVGA3dCmdSetRenderTarget*)ReserveFIFO3D((uint)FIFOCommand.SET_RENDER_TARGET, (uint)sizeof(SVGA3dCmdSetRenderTarget));
                cmd->cid = CurrentContextId;
                cmd->type = SVGA3dRenderTargetType.Depth;
                cmd->target = depthstencil.Surface.SurfaceID;
                
                _driver.CommitFIFOCommand();
            }

            if (depthstencil.StencilPresent)
            {
                SVGA3dCmdSetRenderTarget* cmd = (SVGA3dCmdSetRenderTarget*)ReserveFIFO3D((uint)FIFOCommand.SET_RENDER_TARGET, (uint)sizeof(SVGA3dCmdSetRenderTarget));
                cmd->cid = CurrentContextId;
                cmd->type = SVGA3dRenderTargetType.stencil;
                cmd->target = depthstencil.Surface.SurfaceID;

                _driver.CommitFIFOCommand();
            }
        }

        SyncToFence(InsertFence());
    }

    public void SetViewport(SVGA3dRect rect,float minDepth = 0,float maxDepth = 1)
    {
        if (CheckDXCached())
        {
            uint* cmd = (uint*)ReserveFIFO3D((uint)FIFOCommand.SVGA_3D_CMD_DX_SET_VIEWPORTS, (uint)(16 * sizeof(SVGA3dViewport) + sizeof(uint)));
            *cmd = CurrentContextId;

            SVGA3dViewport* viewports = (SVGA3dViewport*)(cmd + 1);

            SVGA3dViewport vp = new SVGA3dViewport(rect.x, rect.y, rect.w, rect.h, minDepth, maxDepth);
            for (int i = 0; i < 16; i++)
            {
                viewports[i] = vp;
            }
            
            _driver.CommitFIFOCommand();
        }
        else
        {
            SVGA3dCmdSetViewport* cmd = (SVGA3dCmdSetViewport*)ReserveFIFO3D((uint)FIFOCommand.SET_VIEWPORT, (uint)sizeof(SVGA3dCmdSetViewport));
            cmd->cid = CurrentContextId;
            cmd->rect = rect;

            _driver.CommitFIFOCommand();

            SetDepthRange(minDepth,maxDepth);
        }
    }

    void SetDepthRange(float min, float max)
    {
        SVGA3dCmdSetZRange* cmd = (SVGA3dCmdSetZRange*)ReserveFIFO3D((uint)FIFOCommand.SET_ZRANGE, (uint)sizeof(SVGA3dCmdSetZRange));
        cmd->cid = CurrentContextId;
        cmd->range.min = min;
        cmd->range.max = max;

        _driver.CommitFIFOCommand();
    }

    private void ClearRT(Vector4 color,uint id)
    {
        SVGA3dCmdDXClearRenderTargetView* cmd = (SVGA3dCmdDXClearRenderTargetView*)ReserveFIFO3D(
            (uint)FIFOCommand.SVGA_3D_CMD_DX_CLEAR_RENDERTARGET_VIEW,
            (uint)sizeof(SVGA3dCmdDXClearRenderTargetView)
        );
        
        cmd->renderTargetViewId = id;
        cmd->rgba = color;

        _driver.CommitFIFOCommand();
    }
    private void ClearDST(ushort stencil,float depth,uint id,ushort flags)
    {
        SVGA3dCmdDXClearDepthStencilView* cmd = (SVGA3dCmdDXClearDepthStencilView*)ReserveFIFO3D(
            (uint)FIFOCommand.SVGA_3D_CMD_DX_CLEAR_DEPTHSTENCIL_VIEW,
            (uint)sizeof(SVGA3dCmdDXClearDepthStencilView)
        );
        
        cmd->depthStencilViewId = id;
        cmd->stencil = stencil;
        cmd->depth = depth;
        cmd->flags = flags;

        _driver.CommitFIFOCommand();
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

    public void Clear3D(ClearFlags flags, Vector4 color = new(), float depth = 1, uint stencil = 0)
    {
        if (CheckDXCached())
        {
            if (flags.HasFlag(ClearFlags.Color))
            {
                foreach (var item in _gBColorViewTargets)
                {
                    ClearRT(color,item.ViewID);
                }
            }

            if (
                (flags.HasFlag(ClearFlags.Depth) || flags.HasFlag(ClearFlags.Stencil)) && 
                (_gBDepthStencilViewTarget.DepthPresent || _gBDepthStencilViewTarget.StencilPresent)
            )
            {
                ClearFlags dflags = flags & (ClearFlags.Depth | ClearFlags.Stencil);
                ClearDST((ushort)stencil,depth,_gBDepthStencilViewTarget.ViewID,(ushort)dflags);
            }
        }
        else
        {
            var ClearRect = (
                _gBColorViewTargets.Count > 0 ? _gBColorViewTargets[0].Surface.Resolution : 
                (
                    _gBDepthStencilViewTarget.DepthPresent || 
                    _gBDepthStencilViewTarget.StencilPresent
                ) ? _gBDepthStencilViewTarget.Surface.Resolution : 
                throw new ArgumentNullException("No target bound for 3D clear")
            );

            SVGA3dRect* rect;
            BeginClear3D(flags, ColToUint(color), depth, stencil, &rect, 1);
            rect->x = ClearRect.x;
            rect->y = ClearRect.y;
            rect->w = ClearRect.w;
            rect->h = ClearRect.h;

            _driver.CommitFIFOCommand();
        }
    }

    private uint ColToUint(Vector4 col)
    {
        uint a = (uint)(Math.Clamp(col.W, 0f, 1f) * 255f + 0.5f);
        uint r = (uint)(Math.Clamp(col.X, 0f, 1f) * 255f + 0.5f);
        uint g = (uint)(Math.Clamp(col.Y, 0f, 1f) * 255f + 0.5f);
        uint b = (uint)(Math.Clamp(col.Z, 0f, 1f) * 255f + 0.5f);

        return (a << 24) | (r << 16) | (g << 8) | b;
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

        _driver.CommitFIFOCommand();
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

        _driver.CommitFIFOCommand();
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

        _driver.CommitFIFOCommand();
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

        _driver.CommitFIFOCommand();
    }

    public void SetLightEnable(uint index, bool enabled)
    {
        SVGA3dCmdSetLightEnabled* cmd = (SVGA3dCmdSetLightEnabled*)ReserveFIFO3D((uint)FIFOCommand.SETLIGHTENABLE, (uint)sizeof(SVGA3dCmdSetLightEnabled));
        cmd->cid = CurrentContextId;
        cmd->index = index;
        cmd->enabled = enabled ? 1u : 0u;

        _driver.CommitFIFOCommand();
    }

    public void SetLightData(uint index, SVGA3dLightData data)
    {
        SVGA3dCmdSetLightData* cmd = (SVGA3dCmdSetLightData*)ReserveFIFO3D((uint)FIFOCommand.SETLIGHTDATA, (uint)sizeof(SVGA3dCmdSetLightData));
        cmd->cid = CurrentContextId;
        cmd->index = index;
        MemoryOp.MemCopy((byte*)&cmd->data, (byte*)&data, sizeof(SVGA3dLightData));

        _driver.CommitFIFOCommand();
    }

    public void SetMaterial(Face face, SVGA3dMaterial material)
    {
        SVGA3dCmdSetMaterial* cmd = (SVGA3dCmdSetMaterial*)ReserveFIFO3D((uint)FIFOCommand.SETMATERIAL, (uint)sizeof(SVGA3dCmdSetMaterial));
        cmd->cid = CurrentContextId;
        cmd->face = face;
        MemoryOp.MemCopy((byte*)&cmd->material, (byte*)&material, sizeof(SVGA3dMaterial));

        _driver.CommitFIFOCommand();
    }

    #endregion
    #region Direct Data Transfers & Fallbacks

    public GBSurface DefineSurfaceFromImage(int[] image, uint width, uint height,SVGA3dSurfaceFlags flags = SVGA3dSurfaceFlags.SVGA3D_SURFACE_HINT_TEXTURE)
    {
        var surface = DefineSurface(width, height, 1, SVGA3dSurfaceFormat.SVGA3D_A8R8G8B8,flags, 1);

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

            _driver.CommitFIFOCommand();

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
        var surface = DefineSurface(size, 1, 1, SVGA3dSurfaceFormat.SVGA3D_BUFFER,SVGA3dSurfaceFlags.SVGA3D_SURFACE_HINT_INDEXBUFFER | SVGA3dSurfaceFlags.SVGA3D_SURFACE_HINT_VERTEXBUFFER, 1);

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

            _driver.CommitFIFOCommand();

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

    public GBSurface TestDebugBuffer()
    {
        var surface = DefineSurface(1280, 720, 1, SVGA3dSurfaceFormat.SVGA3D_A8R8G8B8);
        
        if (CheckGBCached())
        {
            MemoryOp.MemSet((byte*)surface.MobPtr, 0x30, 1280 * 720 * 4);

            SVGA3dCmdUpdateGBImage* cmd = (SVGA3dCmdUpdateGBImage*)ReserveFIFO3D((uint)FIFOCommand.SVGA_3D_CMD_UPDATE_GB_IMAGE, (uint)sizeof(SVGA3dCmdUpdateGBImage));
            cmd->image = surface.SurfaceID;
            cmd->box.x = 0; cmd->box.y = 0; cmd->box.z = 0;
            cmd->box.w = 1280; cmd->box.h = 720; cmd->box.d = 1;

            _driver.CommitFIFOCommand();

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

        return surface;
    }

    private void BeginPresent(uint sid, SVGA3dCopyRect** rects, uint numRects)
    {
        SVGA3dCmdPresent* cmd = (SVGA3dCmdPresent*)ReserveFIFO3D((uint)FIFOCommand.PRESENT, (uint)sizeof(SVGA3dCmdPresent) + (uint)(numRects * sizeof(SVGA3dCopyRect)));
        cmd->sid = sid;
        *rects = (SVGA3dCopyRect*)&cmd[1];
    }

    public void Present(SVGA3dSurfaceImageId image, SVGA3dRect PresentRect,uint screenTargetId = 0)
    {
        SyncToFence(_lastFence);

        if (CheckGBCached())
        {
            SVGA3dCmdBindGBScreenTarget* bindCmd = (SVGA3dCmdBindGBScreenTarget*)ReserveFIFO3D(
                (uint)FIFOCommand.SVGA_3D_CMD_BIND_GB_SCREENTARGET, 
                (uint)sizeof(SVGA3dCmdBindGBScreenTarget)
            );
            bindCmd->stid = screenTargetId;
            bindCmd->image = image;

            SVGA3dCmdUpdateGBScreenTarget* cmd = (SVGA3dCmdUpdateGBScreenTarget*)ReserveFIFO3D(
                (uint)FIFOCommand.SVGA_3D_CMD_UPDATE_GB_SCREENTARGET, 
                (uint)sizeof(SVGA3dCmdUpdateGBScreenTarget)
            );
            cmd->stid = screenTargetId;
            cmd->rect = PresentRect;

            _driver.CommitFIFOCommand();
        }
        else
        {
            SVGA3dCopyRect* rect;

            BeginPresent(image.sid, &rect, 1);
            MemoryOp.MemSet((byte*)rect, 0, sizeof(SVGA3dCopyRect));
            rect->x = PresentRect.x;
            rect->y = PresentRect.y;
            rect->w = PresentRect.w;
            rect->h = PresentRect.h;

            _driver.CommitFIFOCommand();
        }

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
            SVGA3dCmdReadbackGBImagePartial* cmd = (SVGA3dCmdReadbackGBImagePartial*)ReserveFIFO3D((uint)FIFOCommand.SVGA_3D_CMD_READBACK_GB_IMAGE_PARTIAL, (uint)sizeof(SVGA3dCmdReadbackGBImagePartial));
            cmd->image = surface.SurfaceID;
            cmd->box.x = rect.x; cmd->box.y = rect.y; cmd->box.z = 0;
            cmd->box.w = width; cmd->box.h = height; cmd->box.d = 1;
            cmd->invertBox = inverted ? 1u : 0u;

            _driver.CommitFIFOCommand();

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

            _driver.CommitFIFOCommand();

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

        _driver.CommitFIFOCommand();
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

    bool? _gbcache = null;
    public bool CheckGBCached()
    {
        if (!_gbcache.HasValue)
        {
            _gbcache = (_driver.Capabilities & (uint)Capability.GuestBackedObjects) != 0;
        }

        return _gbcache.Value;
    }

    bool? _dxcache = null;
    public bool CheckDXCached()
    {
        if (!_dxcache.HasValue)
        {
            _dxcache = _driver.QueryCapDev(95) != 0;
        }
        return _dxcache.Value;
    }

    bool? _mdcache = null;
    public bool CheckModernCached()
    {
        if (!_mdcache.HasValue)
        {
            _mdcache = CheckGBCached() && CheckDXCached();
        }
        return _mdcache.Value;
    }

    bool? _sm5cache = null;
    public bool CheckSM5Cached()
    {
        if (!_sm5cache.HasValue)
        {
            _sm5cache = _driver.QueryCapDev(258) != 0;
        }
        return _sm5cache.Value;
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