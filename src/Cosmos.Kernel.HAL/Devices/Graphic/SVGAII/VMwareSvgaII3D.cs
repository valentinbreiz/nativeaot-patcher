using System;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.HAL.Pci;

namespace Cosmos.Kernel.HAL.Devices.Graphic;

public unsafe class VMWareSVGAII3D
{
    public bool Is3DEnabled { get; private set; }
    public uint HW3DVer { get; private set; }

    public VMWareSVGAII3D()
    {
        _device = PciManager.GetDevice(Pci.Enums.VendorId.VmWare, Pci.Enums.DeviceId.SvgaiiAdapter)!;
        if (_device is null)
        {
            throw new Exception("Could not find VmWareSvgaII driver");
        }

        _device.EnableMemory(true);
        uint basePort = _device.BaseAddressBar[0].BaseAddress;

        WriteRegister(Register.ID, (uint)ID.V2);
        if (ReadRegister(Register.ID) != (uint)ID.V2)
        {
            return;
        }

        VideoMemory = new MemoryBlock(ReadRegister(Register.FrameBufferStart), ReadRegister(Register.VRamSize));
        Capabilities = ReadRegister(Register.Capabilities);

        InitializeFIFO();
    }


    private uint _contextId;

    private uint GetNextContextId() => ++_contextId;


    private uint _surfaceId;

    private uint GetNextSurfaceId() => ++_surfaceId;

    private bool _fifoFenceSupported;
    private uint _guestFenceCounter = 1;

    private void SyncToFence(uint fence)
    {
        if (_fifoFenceSupported && _fifoMemory != null)
        {
            while (ReadFifo3D(Register3D.SVGA_FIFO_FENCE) < fence) { }
        }
        else
        {
            WriteRegister(Register.Sync, 1);
            while (ReadRegister(Register.Busy) != 0) { }
        }
    }

    private uint InsertFence()
    {
        uint fence = ++_guestFenceCounter;

        if (_fifoFenceSupported && _fifoMemory != null)
        {
            WriteFifo3D(Register3D.SVGA_FIFO_FENCE, fence);
        }
        else
        {
            WriteRegister(Register.Sync, fence);
        }

        return fence;
    }

    public void* ReserveFIFO3D(uint cmd, uint cmdSize)
    {
        SVGA3dCmdHeader* header;

        header = (SVGA3dCmdHeader*)ReserveFIFO((uint)sizeof(SVGA3dCmdHeader) + cmdSize);
        header->id = cmd;
        header->size = cmdSize;

        return &header[1];
    }

    private void BeginSurfaceDefinition(
        uint sid,
        SVGA3dSurfaceFlags flags,
        SVGA3dSurfaceFormat format,
        ref uint* faces,
        out SVGA3dSize* mipSizes,
        uint numMipSizes)
    {
        SVGA3dCmdDefineSurface* cmd = (SVGA3dCmdDefineSurface*)ReserveFIFO3D(
            (uint)FIFOCommand.DEFINE_SURFACE,
            (uint)sizeof(SVGA3dCmdDefineSurface) + (uint)(numMipSizes * sizeof(SVGA3dSize)));

        cmd->sid = sid;
        cmd->flags = flags;
        cmd->format = format;

        faces = &cmd->face[0];
        mipSizes = (SVGA3dSize*)&cmd[1];

        MemoryOp.MemSet((byte*)faces, 0, sizeof(uint) * 6);
        MemoryOp.MemSet((byte*)mipSizes, 0, sizeof(SVGA3dSize) * (int)numMipSizes);
    }


    public SVGA3dSurfaceImageId DefineSurface(uint width, uint height, SVGA3dSurfaceFormat format)
    {
        uint sid = GetNextSurfaceId();
        SVGA3dSize* mipSizes;
        uint* faces = null;

        BeginSurfaceDefinition(sid, 0, format, ref faces, out mipSizes, 1);

        faces[0] = 1;
        mipSizes[0].width = width;
        mipSizes[0].height = height;
        mipSizes[0].depth = 1;

        WaitForFifo();

        return new() { sid = sid, face = 0, mipmap = 0 };
    }

    public void SetRenderTarget(uint cid, SVGA3dRenderTargetType type, SVGA3dSurfaceImageId target)
    {
        SVGA3dCmdSetRenderTarget* cmd;
        cmd = (SVGA3dCmdSetRenderTarget*)ReserveFIFO3D((uint)FIFOCommand.SET_RENDER_TARGET, (uint)sizeof(SVGA3dCmdSetRenderTarget));
        cmd->cid = cid;
        cmd->type = type;
        cmd->target = target;
        WaitForFifo();
    }

    public void SetViewport(uint cid, SVGA3dRect rect)
    {
        SVGA3dCmdSetViewport* cmd;
        cmd = (SVGA3dCmdSetViewport*)ReserveFIFO3D((uint)FIFOCommand.SET_VIEWPORT, (uint)sizeof(SVGA3dCmdSetViewport));
        cmd->cid = cid;
        cmd->rect = rect;
        WaitForFifo();
    }

    public void SetDepthRange(uint cid, float min, float max)
    {
        SVGA3dCmdSetZRange* cmd;
        cmd = (SVGA3dCmdSetZRange*)ReserveFIFO3D((uint)FIFOCommand.SET_ZRANGE, (uint)sizeof(SVGA3dCmdSetZRange));
        cmd->cid = cid;
        cmd->range.min = min;
        cmd->range.max = max;
        WaitForFifo();
    }

    private void BeginClear3D(
        uint cid,
        ClearFlags flags,
        uint color,
        float depth,
        uint stencil,
        SVGA3dRect** rects,
        uint numRects)
    {
        SVGA3dCmdClear* cmd;
        cmd = (SVGA3dCmdClear*)ReserveFIFO3D((uint)FIFOCommand.CLEAR, (uint)sizeof(SVGA3dCmdClear) + (uint)(numRects * sizeof(SVGA3dRect)));

        cmd->cid = cid;
        cmd->flag = flags;
        cmd->color = color;
        cmd->depth = depth;
        cmd->stencil = stencil;
        *rects = (SVGA3dRect*)&cmd[1];
    }

    public void Clear3D(uint cid, ClearFlags flags, SVGA3dRect ClearRect, uint color = 0, float depth = 1, uint stencil = 0)
    {
        SVGA3dRect* rect;

        BeginClear3D(cid, flags, color, depth, stencil, &rect, 1);
        rect->x = ClearRect.x;
        rect->y = ClearRect.y;
        rect->w = ClearRect.w;
        rect->h = ClearRect.h;
        WaitForFifo();
    }

    private void BeginPresent(uint sid, SVGA3dCopyRect** rects, uint numRects)
    {
        SVGA3dCmdPresent* cmd;
        cmd = (SVGA3dCmdPresent*)ReserveFIFO3D((uint)FIFOCommand.PRESENT, (uint)sizeof(SVGA3dCmdPresent) + (uint)(numRects * sizeof(SVGA3dCopyRect)));
        cmd->sid = sid;
        *rects = (SVGA3dCopyRect*)&cmd[1];
    }

    private uint _lastFence = 1;

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
        WaitForFifo();

        _lastFence = InsertFence();
    }

    public int[]? PresentToImage(SVGA3dSurfaceImageId image, SVGA3dRect rect)
    {
        uint width = rect.w;
        uint height = rect.h;

        if (width == 0 || height == 0)
        {
            return null;
        }

        const uint bytesPerPixel = 4u;
        uint size = width * height * bytesPerPixel;

        void* buffer = SVGA3DUtil_AllocDMABuffer(size, out SVGAGuestPtr gPtr);

        SVGA3dGuestImage guestImage;
        guestImage.ptr = gPtr;
        guestImage.pitch = 0;

        SVGA3dSurfaceImageId hostImage = image;

        SVGA3dCopyBox* boxes;
        BeginSurfaceDMA(&guestImage, &hostImage, SVGA3dTransferType.SVGA3D_READ_HOST_VRAM, &boxes, 1);

        boxes[0].x = rect.x;
        boxes[0].y = rect.y;
        boxes[0].w = width;
        boxes[0].h = height;
        boxes[0].d = 1;

        WaitForFifo();

        uint fence = InsertFence();
        SyncToFence(fence);

        int pixelCount = (int)(width * height);
        int[] pixels = new int[pixelCount];

        fixed (int* pDest = &pixels[0])
        {
            MemoryOp.MemCopy((byte*)pDest, (byte*)buffer, (int)size);
        }

        return pixels;
    }


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
        WaitForFifo();
    }

    public uint CreateStaticArrayBuffer<T>(T[] data) where T : unmanaged
    {
        uint size = (uint)(data.Length * sizeof(T));

        uint sid = DefineSurface(size, 1, SVGA3dSurfaceFormat.SVGA3D_BUFFER).sid;

        SVGAGuestPtr gPtr;
        void* buffer = SVGA3DUtil_AllocDMABuffer(size, out gPtr);

        fixed (T* pData = &data[0])
        {
            MemoryOp.MemCopy((byte*)buffer, (byte*)pData, (int)size);
        }

        SurfaceDMA2D(sid, &gPtr, SVGA3dTransferType.SVGA3D_WRITE_HOST_VRAM, size, 1);

        return sid;
    }


    public uint TestDebugBuffer()
    {
        void* buffer;
        SVGAGuestPtr gPtr;
        uint sid = DefineSurface(1280, 720, SVGA3dSurfaceFormat.SVGA3D_A8R8G8B8).sid;

        buffer = SVGA3DUtil_AllocDMABuffer(1280 * 720 * 4, out gPtr);

        MemoryOp.MemSet((byte*)buffer, 0x30, 1280 * 720 * 4);

        SurfaceDMA2D(sid, &gPtr, SVGA3dTransferType.SVGA3D_WRITE_HOST_VRAM, 1280, 720);

        return sid;
    }

    public SVGA3dSurfaceImageId DefineSurfaceFromImage(int[] image, uint width, uint height)
    {
        void* buffer;
        SVGAGuestPtr gPtr;
        uint sid = DefineSurface(width, height, SVGA3dSurfaceFormat.SVGA3D_A8R8G8B8).sid;

        buffer = SVGA3DUtil_AllocDMABuffer(width * height * sizeof(int), out gPtr);

        fixed (int* rawDataPtr = image)
        {
            MemoryOp.MemCopy((byte*)buffer, (byte*)rawDataPtr, image.Length * sizeof(int));
        }

        SurfaceDMA2D(sid, &gPtr, SVGA3dTransferType.SVGA3D_WRITE_HOST_VRAM, width, height);

        return new() { sid = sid, face = 0, mipmap = 0 };
    }

    private void BeginSetRenderState(uint cid, SVGA3dRenderState** states, uint numstates)
    {
        SVGA3dCmdSetRenderState* cmd;
        cmd = (SVGA3dCmdSetRenderState*)ReserveFIFO3D((uint)FIFOCommand.SETRENDERSTATE, (uint)(sizeof(SVGA3dCmdSetRenderState) + sizeof(SVGA3dRenderState) * numstates));

        cmd->cid = cid;

        *states = (SVGA3dRenderState*)&cmd[1];
    }

    public void SetRenderState(uint cid, SVGA3dRenderState[] states)
    {
        SVGA3dRenderState* rs;
        BeginSetRenderState(cid, &rs, (uint)states.Length);

        fixed (SVGA3dRenderState* statesPtr = &states[0])
        {
            MemoryOp.MemCopy((byte*)rs, (byte*)statesPtr, sizeof(SVGA3dRenderState) * states.Length);
        }

        WaitForFifo();
    }

    private void BeginSetTextureState(uint cid, SVGA3dTextureState** states, uint numStates)
    {
        SVGA3dCmdSetTextureState* cmd;
        cmd = (SVGA3dCmdSetTextureState*)ReserveFIFO3D((uint)FIFOCommand.SETTEXTURESTATE, (uint)(sizeof(SVGA3dCmdSetTextureState) + sizeof(SVGA3dTextureState) * numStates));
        cmd->cid = cid;

        *states = (SVGA3dTextureState*)&cmd[1];
    }

    public void SetTextureState(uint cid, SVGA3dTextureState[] states)
    {
        SVGA3dTextureState* ts;
        BeginSetTextureState(cid, &ts, (uint)states.Length);

        fixed (SVGA3dTextureState* statesPtr = &states[0])
        {
            MemoryOp.MemCopy((byte*)ts, (byte*)statesPtr, sizeof(SVGA3dTextureState) * states.Length);
        }

        WaitForFifo();
    }

    private void BeginDrawPrimitives(
        uint cid,
        SVGA3dVertexDecl** decls,
        uint numVertexDecls,
        SVGA3dPrimitiveRange** ranges,
        uint numRanges)
    {
        SVGA3dCmdDrawPrimitives* cmd;
        SVGA3dVertexDecl* declArray;
        SVGA3dPrimitiveRange* rangeArray;
        uint declSize = (uint)sizeof(SVGA3dVertexDecl) * numVertexDecls;
        uint rangeSize = (uint)sizeof(SVGA3dPrimitiveRange) * numRanges;

        cmd = (SVGA3dCmdDrawPrimitives*)ReserveFIFO3D((uint)FIFOCommand.DRAW_PRIMITIVES, (uint)sizeof(SVGA3dCmdDrawPrimitives) + declSize + rangeSize);

        cmd->cid = cid;
        cmd->numVertexDecls = numVertexDecls;
        cmd->numRanges = numRanges;

        declArray = (SVGA3dVertexDecl*)&cmd[1];
        rangeArray = (SVGA3dPrimitiveRange*)&declArray[numVertexDecls];

        MemoryOp.MemSet((byte*)declArray, 0, (int)declSize);
        MemoryOp.MemSet((byte*)rangeArray, 0, (int)rangeSize);

        *decls = declArray;
        *ranges = rangeArray;
    }

    public void DrawPrimitives(
        uint cid,
        SVGA3dVertexDecl[] decls,
        SVGA3dPrimitiveRange[] ranges)
    {
        SVGA3dVertexDecl* vdecls;
        SVGA3dPrimitiveRange* pranges;
        BeginDrawPrimitives(cid, &vdecls, (uint)decls.Length, &pranges, (uint)ranges.Length);

        fixed (SVGA3dVertexDecl* statesPtr = &decls[0])
        {
            MemoryOp.MemCopy((byte*)vdecls, (byte*)statesPtr, sizeof(SVGA3dVertexDecl) * decls.Length);
        }
        fixed (SVGA3dPrimitiveRange* statesPtr = &ranges[0])
        {
            MemoryOp.MemCopy((byte*)pranges, (byte*)statesPtr, sizeof(SVGA3dPrimitiveRange) * ranges.Length);
        }

        WaitForFifo();
    }

    private void InternalSetTransform(uint cid, SVGA3dTransformType type, float* matrix)
    {
        SVGA3dCmdSetTransform* cmd;
        cmd = (SVGA3dCmdSetTransform*)ReserveFIFO3D((uint)FIFOCommand.SETTRANSFORM, (uint)sizeof(SVGA3dCmdSetTransform));
        cmd->cid = cid;
        cmd->type = type;

        MemoryOp.MemCopy((byte*)&cmd->matrix[0], (byte*)matrix, sizeof(float) * 16);
        WaitForFifo();
    }

    public void SetTransform<T>(uint cid, SVGA3dTransformType type, T matrix4x4)
    {
        if (sizeof(T) == 16 * sizeof(float))
        {
            InternalSetTransform(cid, type, (float*)&matrix4x4);
        }
        else
        {
            throw new ArgumentException("Matrix must be 4x4 float");
        }
    }

    private uint _shaderIdVS = 0;
    private uint _shaderIdPS = 0;

    uint GetNextShaderId(SVGA3dShaderType type)
    {
        switch (type)
        {
            case SVGA3dShaderType.SVGA3D_SHADERTYPE_VS: return _shaderIdVS++;
            case SVGA3dShaderType.SVGA3D_SHADERTYPE_PS: return _shaderIdPS++;

            default: return 0;
        }
    }

    public uint DefineShader(uint cid, SVGA3dShaderType type, byte[] bytecode)
    {
        SVGA3dCmdDefineShader* cmd;

        cmd = (SVGA3dCmdDefineShader*)ReserveFIFO3D((uint)FIFOCommand.SHADER_DEFINE, (uint)sizeof(SVGA3dCmdDefineShader) + (uint)bytecode.Length);

        var shid = GetNextShaderId(type);

        cmd->cid = cid;
        cmd->shid = shid;
        cmd->type = type;

        fixed (byte* bytecodePtr = &bytecode[0])
        {
            MemoryOp.MemCopy((byte*)&cmd[1], bytecodePtr, bytecode.Length);
        }

        WaitForFifo();

        return shid;
    }

    public void SetShader(uint cid, SVGA3dShaderType type, uint shid)
    {
        SVGA3dCmdSetShader* cmd;

        cmd = (SVGA3dCmdSetShader*)ReserveFIFO3D((uint)FIFOCommand.SET_SHADER, (uint)sizeof(SVGA3dCmdSetShader));
        cmd->cid = cid;
        cmd->type = type;
        cmd->shid = shid;
        WaitForFifo();
    }

    public void SetShaderUniform<T>(uint cid, uint reg, SVGA3dShaderType type, SVGA3dShaderConstType ctype, T value) where T : unmanaged
    {
        SVGA3dCmdSetShaderConst* cmd;
        cmd = (SVGA3dCmdSetShaderConst*)ReserveFIFO3D((uint)FIFOCommand.SET_SHADER_CONST, (uint)sizeof(SVGA3dCmdSetShaderConst));

        cmd->cid = cid;
        cmd->reg = reg;
        cmd->type = type;
        cmd->ctype = ctype;

        cmd->values[0] = 0;
        cmd->values[1] = 0;
        cmd->values[2] = 0;
        cmd->values[3] = 0;

        byte* src = (byte*)&value;
        byte* dst = (byte*)cmd->values;

        int size = sizeof(T);
        if (size > 16)
        {
            size = 16;
        }
        for (int i = 0; i < size; i++)
        {
            dst[i] = src[i];
        }

        WaitForFifo();
    }


    public uint DefineContext()
    {
        uint cid = GetNextContextId();

        SVGA3dCmdDefineContext* cmd;
        cmd = (SVGA3dCmdDefineContext*)ReserveFIFO3D((uint)FIFOCommand.DEFINE_CONTEXT, (uint)sizeof(SVGA3dCmdDefineContext));
        cmd->cid = cid;

        WaitForFifo();
        return cid;
    }

    private static SVGAGuestPtr s_nextPtr = new SVGAGuestPtr { gmrId = 0, offset = 0 };

    private const uint SVGA_GMR_FRAMEBUFFER = 0xFFFFFFFEu;

    public unsafe void* SVGA3DUtil_AllocDMABuffer(uint size, out SVGAGuestPtr ptr)
    {
        uint alignedSize = (size + 3u) & ~3u;
        if ((Capabilities & (uint)Capability.Gmr) == 0)
        {
            throw new InvalidOperationException("SVGA device does not support GMR â€” cannot allocate framebuffer-backed guest pointer.");
        }

        if (s_nextPtr.offset + alignedSize > VideoMemory.Size)
        {
            throw new OutOfMemoryException("Not enough VRAM for framebuffer-backed buffer");
        }

        ptr = new SVGAGuestPtr
        {
            gmrId = SVGA_GMR_FRAMEBUFFER,
            offset = s_nextPtr.offset
        };

        void* buffer = (void*)(VideoMemory.Base + s_nextPtr.offset);

        s_nextPtr.offset += alignedSize;

        return buffer;
    }


    /// <summary>
    /// Initialize FIFO.
    /// </summary>
    public void InitializeFIFO()
    {
        _fifoMemory = new MemoryBlock(ReadRegister(Register.MemStart), ReadRegister(Register.MemSize));
        _fifoMemory[(uint)FIFO.Min] = (uint)Register.FifoNumRegisters * sizeof(uint);
        _fifoMemory[(uint)FIFO.Max] = _fifoMemory.Size;
        _fifoMemory[(uint)FIFO.NextCmd] = _fifoMemory[(uint)FIFO.Min];
        _fifoMemory[(uint)FIFO.Stop] = _fifoMemory[(uint)FIFO.Min];

        if (((Capabilities & 0x00008000) != 0) &&
            ((Capabilities & (uint)Capability.Cap3D) != 0) &&
            (_fifoMemory[(uint)FIFO.Min] > ((uint)Register3D.SVGA_FIFO_3D_HWVERSION << 2)))
        {
            WriteFifo3D(Register3D.SVGA_FIFO_3D_HWVERSION, ((2u) << 16) | (1u & 0xFFu));

            Is3DEnabled = true;

            if ((Capabilities & (1 << 8)) != 0)
            {
                HW3DVer = ReadFifo3D(Register3D.SVGA_FIFO_3D_HWVERSION_REVISED);

                if (HW3DVer < (((2u) << 16) | (0u & 0xFFu)))
                {
                    Is3DEnabled = false;
                }
            }
            else
            {
                Is3DEnabled = false;
            }
        }
        else
        {
            Is3DEnabled = false;
        }


        WriteRegister((Register)1, 1);
        WriteRegister(Register.ConfigDone, 1);
    }

    private uint ReadFifo3D(Register3D reg)
    {
        return _fifoMemory[(uint)reg << 2];
    }

    private void WriteFifo3D(Register3D reg, uint value)
    {
        _fifoMemory[(uint)reg << 2] = value;
    }


    /// <summary>
    /// Set video mode.
    /// </summary>
    /// <param name="width">Width.</param>
    /// <param name="height">Height.</param>
    /// <param name="depth">Depth.</param>
    public void SetMode(uint width, uint height, uint depth = 32)
    {
        //Disable the Driver before writing new values and initiating it again to avoid a memory exception
        //Disable();

        // Depth is color depth in bytes.
        _depth = depth / 8;
        _width = width;
        _height = height;
        WriteRegister(Register.Width, width);
        WriteRegister(Register.Height, height);
        WriteRegister(Register.BitsPerPixel, depth);
        Enable();
        InitializeFIFO();

        FrameSize = ReadRegister(Register.FrameBufferSize);
        FrameOffset = ReadRegister(Register.FrameBufferOffset);
    }

    /// <summary>
    /// Write register.
    /// </summary>
    /// <param name="register">A register.</param>
    /// <param name="value">A value.</param>
    public void WriteRegister(Register register, uint value)
    {
        _device.WriteRegister32((byte)IOPortOffset.Index, (uint)register);
        _device.WriteRegister32((byte)IOPortOffset.Value, value);
    }

    /// <summary>
    /// Read register.
    /// </summary>
    /// <param name="register">A register.</param>
    /// <returns>uint value.</returns>
    public uint ReadRegister(Register register)
    {
        _device.WriteRegister32((byte)IOPortOffset.Index, (uint)register);
        return _device.ReadRegister32((byte)IOPortOffset.Value);
    }

    /// <summary>
    /// Get FIFO.
    /// </summary>
    /// <param name="cmd">FIFO command.</param>
    /// <returns>uint value.</returns>
    public uint GetFIFO(FIFO cmd) => _fifoMemory[(uint)cmd];

    /// <summary>
    /// Set FIFO.
    /// </summary>
    /// <param name="cmd">Command.</param>
    /// <param name="value">Value.</param>
    /// <returns></returns>
    public uint SetFIFO(FIFO cmd, uint value) => _fifoMemory[(uint)cmd] = value;

    /// <summary>
    /// Wait for FIFO.
    /// </summary>
    public void WaitForFifo()
    {
        WriteRegister(Register.Sync, 1);
        while (ReadRegister(Register.Busy) != 0) { }
    }

    /// <summary>
    /// Write to FIFO.
    /// </summary>
    /// <param name="value">Value to write.</param>
    public void WriteToFifo(uint value)
    {
        if (GetFIFO(FIFO.NextCmd) == GetFIFO(FIFO.Max) - 4 && GetFIFO(FIFO.Stop) == GetFIFO(FIFO.Min) ||
            GetFIFO(FIFO.NextCmd) + 4 == GetFIFO(FIFO.Stop))
        {
            WaitForFifo();
        }

        SetFIFO((FIFO)GetFIFO(FIFO.NextCmd), value);
        SetFIFO(FIFO.NextCmd, GetFIFO(FIFO.NextCmd) + 4);

        if (GetFIFO(FIFO.NextCmd) == GetFIFO(FIFO.Max))
        {
            SetFIFO(FIFO.NextCmd, GetFIFO(FIFO.Min));
        }
    }

    public void* ReserveFIFO(uint bytes)
    {
        uint next = GetFIFO(FIFO.NextCmd);
        uint stop = GetFIFO(FIFO.Stop);
        uint min = GetFIFO(FIFO.Min);
        uint max = GetFIFO(FIFO.Max);

        uint space;
        if (next >= stop)
        {
            space = (max - next) + (stop - min);
        }
        else
        {
            space = stop - next;
        }

        // Wait if not enough contiguous space
        while (space < bytes)
        {
            WaitForFifo(); // give the SVGA _device time to consume FIFO
            next = GetFIFO(FIFO.NextCmd);
            stop = GetFIFO(FIFO.Stop);
            if (next >= stop)
            {
                space = (max - next) + (stop - min);
            }
            else
            {
                space = stop - next;
            }
        }

        // Make sure contiguous region fits before end of buffer
        if (next + bytes > max)
        {
            // wrap to beginning of buffer
            SetFIFO(FIFO.NextCmd, min);
            next = min;
        }

        // Compute pointer into memory block
        void* ptr = (void*)(_fifoMemory.Base + next); // hypothetical: Address property gives base address

        // Advance NEXT_CMD
        uint newNext = next + bytes;
        SetFIFO(FIFO.NextCmd, (newNext == max ? min : newNext));

        return ptr;
    }


    /// <summary>
    /// Update FIFO.
    /// </summary>
    /// <param name="x">X coordinate.</param>
    /// <param name="y">Y coordinate.</param>
    /// <param name="width">Width.</param>
    /// <param name="height">Height.</param>
    public void Update(uint x, uint y, uint width, uint height)
    {
        WriteToFifo((uint)FIFOCommand.Update);
        WriteToFifo(x);
        WriteToFifo(y);
        WriteToFifo(width);
        WriteToFifo(height);
        WaitForFifo();
    }

    /// <summary>
    /// Update video memory.
    /// </summary>
    public void DoubleBufferUpdate()
    {
        VideoMemory.MoveDown(FrameOffset, FrameSize, FrameSize);
        Update(0, 0, _width, _height);
    }

    /// <summary>
    /// Set pixel.
    /// </summary>
    /// <param name="x">X coordinate.</param>
    /// <param name="y">Y coordinate.</param>
    /// <param name="color">Color.</param>
    /// <exception cref="Exception">Thrown on memory access violation.</exception>
    public void SetPixel(uint x, uint y, uint color)
    {
        VideoMemory[(y * _width + x) * _depth + FrameSize] = color;
    }

    /// <summary>
    /// Get pixel.
    /// </summary>
    /// <param name="x">X coordinate.</param>
    /// <param name="y">Y coordinate.</param>
    /// <returns>uint value.</returns>
    /// <exception cref="Exception">Thrown on memory access violation.</exception>
    public uint GetPixel(uint x, uint y)
    {
        return VideoMemory[(y * _width + x) * _depth + FrameSize];
    }

    /// <summary>
    /// Clear screen to specified color.
    /// </summary>
    /// <param name="color">Color.</param>
    /// <exception cref="Exception">Thrown on memory access violation.</exception>
    /// <exception cref="NotImplementedException">Thrown if VMWare SVGA 2 has no rectangle copy capability</exception>
    public void Clear(uint color)
    {
        VideoMemory.Fill(FrameSize, FrameSize, color);
    }

    /// <summary>
    /// Copy rectangle.
    /// </summary>
    /// <param name="x">Source X coordinate.</param>
    /// <param name="y">Source Y coordinate.</param>
    /// <param name="newX">Destination X coordinate.</param>
    /// <param name="newY">Destination Y coordinate.</param>
    /// <param name="width">Width.</param>
    /// <param name="height">Height.</param>
    /// <exception cref="NotImplementedException">Thrown if VMWare SVGA 2 has no rectangle copy capability</exception>
    public void Copy(uint x, uint y, uint newX, uint newY, uint width, uint height)
    {
        if ((Capabilities & (uint)Capability.RectCopy) != 0)
        {
            WriteToFifo((uint)FIFOCommand.RECT_COPY);
            WriteToFifo(x);
            WriteToFifo(y);
            WriteToFifo(newX);
            WriteToFifo(newY);
            WriteToFifo(width);
            WriteToFifo(height);
            WaitForFifo();
        }
        else
        {
            throw new NotImplementedException("VMWareSVGAII Copy()");
        }
    }

    /// <summary>
    /// Fill rectangle.
    /// </summary>
    /// <param name="x">X coordinate.</param>
    /// <param name="y">Y coordinate.</param>
    /// <param name="width">Width.</param>
    /// <param name="height">Height.</param>
    /// <param name="color">Color.</param>
    /// <exception cref="Exception">Thrown on memory access violation.</exception>
    /// <exception cref="NotImplementedException">Thrown if VMWare SVGA 2 has no rectangle copy capability</exception>
    public void Fill(uint x, uint y, uint width, uint height, uint color)
    {
        if ((Capabilities & (uint)Capability.RectFill) != 0)
        {
            WriteToFifo((uint)FIFOCommand.RECT_FILL);
            WriteToFifo(color);
            WriteToFifo(x);
            WriteToFifo(y);
            WriteToFifo(width);
            WriteToFifo(height);
            WaitForFifo();
        }
        else
        {
            if ((Capabilities & (uint)Capability.RectCopy) != 0)
            {
                // fill first line and copy it to all other
                uint xTarget = x + width;
                uint yTarget = y + height;

                for (uint xTmp = x; xTmp < xTarget; xTmp++)
                {
                    SetPixel(xTmp, y, color);
                }
                // refresh first line for copy process
                Update(x, y, width, 1);
                for (uint yTmp = y + 1; yTmp < yTarget; yTmp++)
                {
                    Copy(x, y, x, yTmp, width, 1);
                }
            }
            else
            {
                uint xTarget = x + width;
                uint yTarget = y + height;
                for (uint xTmp = x; xTmp < xTarget; xTmp++)
                {
                    for (uint yTmp = y; yTmp < yTarget; yTmp++)
                    {
                        SetPixel(xTmp, yTmp, color);
                    }
                }
                Update(x, y, width, height);
            }
        }
    }

    /// <summary>
    /// Define cursor.
    /// </summary>
    public void DefineCursor()
    {
        WaitForFifo();
        WriteToFifo((uint)FIFOCommand.DEFINE_CURSOR);
        WriteToFifo(0); // ID
        WriteToFifo(0); // Hotspot X
        WriteToFifo(0); // Hotspot Y
        WriteToFifo(2);
        WriteToFifo(2);
        WriteToFifo(1);
        WriteToFifo(1);

        for (int i = 0; i < 4; i++)
        {
            WriteToFifo(0);
        }

        for (int i = 0; i < 4; i++)
        {
            WriteToFifo(0xFFFFFF);
        }

        WaitForFifo();
    }

    /// <summary>
    /// Define alpha cursor.
    /// </summary>
    public void DefineAlphaCursor(uint width, uint height, int[] data)
    {
        WaitForFifo();
        WriteToFifo((uint)FIFOCommand.DEFINE_ALPHA_CURSOR);
        WriteToFifo(0); // ID
        WriteToFifo(0); // Hotspot X
        WriteToFifo(0); // Hotspot Y
        WriteToFifo(width); // Width
        WriteToFifo(height); // Height

        for (int i = 0; i < data.Length; i++)
        {
            WriteToFifo((uint)data[i]);
        }

        WaitForFifo();
    }

    /// <summary>
    /// Enable the SVGA Driver, only needed after Disable() has been called.
    /// </summary>
    public void Enable()
    {
        WriteRegister(Register.Enable, 1);
    }

    /// <summary>
    /// Disable the SVGA Driver, returns to text mode.
    /// </summary>
    public void Disable()
    {
        WriteRegister(Register.Enable, 0);
    }

    /// <summary>
    /// Sets the cursor position and draws it.
    /// </summary>
    /// <param name="visible">Visible.</param>
    /// <param name="x">X coordinate.</param>
    /// <param name="y">Y coordinate.</param>
    public void SetCursor(bool visible, uint x, uint y)
    {
        WriteRegister(Register.CursorOn, (uint)(visible ? 1 : 0));
        WriteRegister(Register.CursorX, x);
        WriteRegister(Register.CursorY, y);
        WriteRegister(Register.CursorCount, ReadRegister(Register.CursorCount) + 1);
    }



    /// <summary>
    /// Video memory block.
    /// </summary>
    public MemoryBlock VideoMemory { get; } = null!;

    /// <summary>
    /// FIFO memory block.
    /// </summary>
    private MemoryBlock _fifoMemory = null!;

    /// <summary>
    /// PCI _device.
    /// </summary>
    private readonly PciDevice _device;

    /// <summary>
    /// Height.
    /// </summary>
    private uint _height;

    /// <summary>
    /// Width.
    /// </summary>
    private uint _width;

    /// <summary>
    /// Depth.
    /// </summary>
    private uint _depth;

    /// <summary>
    /// Capabilities.
    /// </summary>
    public uint Capabilities { get; }

    public uint FrameSize { get; private set; }
    public uint FrameOffset { get; private set; }

}

