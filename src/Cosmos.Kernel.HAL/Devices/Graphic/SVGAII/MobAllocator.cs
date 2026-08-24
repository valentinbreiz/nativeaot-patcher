using System;
using System.Numerics;
using Cosmos.Kernel.Boot.Limine;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.Core.Memory.Heap;

namespace Cosmos.Kernel.HAL.Devices.Graphic.SVGAII;

struct MobAllocation
{
    public ulong[] DataPages;
    public ulong[] PtPages;
}


public static unsafe class MobAllocator
{
    static readonly Dictionary<uint, MobAllocation> s_mobAllocations = new();
    static readonly Stack<ulong> s_freePagePool = new();

    static bool s_oTablesAllocated = false;

    static ulong HhdmOffset => Limine.HHDM.Response != null ? Limine.HHDM.Response->Offset : 0;

    static uint s_mobIdCounter = 0;
    static uint GetNextMobId() => ++s_mobIdCounter;

    static ulong AllocPhysicalPage()
    {
        if (s_freePagePool.Count > 0)
        {
            return s_freePagePool.Pop();
        }

        ulong raw = (ulong)PageAllocator.AllocPages(PageType.HeapSmall, zero: true);
        ulong phys = raw - HhdmOffset;

        Serial.WriteString("[MobAlloc] raw=0x"); Serial.WriteHex(raw);
        Serial.WriteString(" hhdm=0x"); Serial.WriteHex(HhdmOffset);
        Serial.WriteString(" phys=0x"); Serial.WriteHex(phys);
        Serial.WriteString(" ppn=0x"); Serial.WriteHex(phys / 4096u);
        Serial.WriteString("\n");

        return phys;
    }

    static void FreePhysicalPage(ulong physAddr) => s_freePagePool.Push(physAddr);

    public static uint DefineGBMob(this VMWareSVGAII3D _driver3D, SvgaIIDriver _driver, uint sizeInBytes, out void* buffer, out SVGAGuestPtr gPtr)
    {
        uint mobid = GetNextMobId();
        uint alignedSize = (sizeInBytes + 4095u) & ~4095u;
        uint numPages = alignedSize / 4096u;

        if (numPages > 1024)
        {
            throw new NotSupportedException(
                "MOB exceeds PTDEPTH_1 capacity (4 MB / 1024 pages) — needs PTDEPTH_2, not implemented."
            );
        }

        ulong[] dataPages = new ulong[numPages];
        for (uint i = 0; i < numPages; i++)
        {
            dataPages[i] = AllocPhysicalPage();
        }

        buffer = (void*)(HhdmOffset + dataPages[0]);

        gPtr = default;

        MobFormat ptDepth;
        uint basePPN;
        ulong[] ptPages;

        if (numPages == 1)
        {
            ptDepth = MobFormat.PTDEPTH_0;
            basePPN = (uint)(dataPages[0] / 4096u);
            ptPages = Array.Empty<ulong>();
        }
        else
        {
            ptDepth = MobFormat.PTDEPTH_1;

            ulong ptPage = AllocPhysicalPage();
            ptPages = [ptPage];

            uint* pageTable = (uint*)(HhdmOffset + ptPage);
            for (uint i = 0; i < numPages; i++)
            {
                pageTable[i] = (uint)(dataPages[i] / 4096u);
            }

            basePPN = (uint)(ptPage / 4096u);
        }

        s_mobAllocations[mobid] = new MobAllocation { DataPages = dataPages, PtPages = ptPages };

        SVGA3dCmdDefineGBMob* cmd = (SVGA3dCmdDefineGBMob*)_driver3D.ReserveFIFO3D(
            (uint)FIFOCommand.SVGA_3D_CMD_DEFINE_GB_MOB, (uint)sizeof(SVGA3dCmdDefineGBMob)
        );

        cmd->mobid = mobid;
        cmd->ptDepth = ptDepth;
        cmd->basePPN = basePPN;
        cmd->sizeInBytes = sizeInBytes;

        _driver.CommitFIFOCommand();

        return mobid;
    }

    public static void DestroyGBMob(this VMWareSVGAII3D _driver3D, SvgaIIDriver _driver, uint mobid)
    {
        SVGA3dCmdDestroyGBMob* cmd = (SVGA3dCmdDestroyGBMob*)_driver3D.ReserveFIFO3D(
            (uint)FIFOCommand.SVGA_3D_CMD_DESTROY_GB_MOB, (uint)sizeof(SVGA3dCmdDestroyGBMob)
        );

        cmd->mobid = mobid;

        _driver.CommitFIFOCommand();
        _driver.WaitForFifo();

        if (s_mobAllocations.Remove(mobid, out var alloc))
        {
            foreach (ulong page in alloc.DataPages)
            {
                FreePhysicalPage(page);
            }
            foreach (ulong page in alloc.PtPages)
            {
                FreePhysicalPage(page);
            }
        }
    }

    static void SetupOneOTable(this VMWareSVGAII3D _driver3D, SvgaIIDriver _driver, OTableType type, uint entrySize, uint count)
    {
        uint sizeInBytes = entrySize * count;
        uint alignedSize = (sizeInBytes + 4095u) & ~4095u;
        uint numPages = alignedSize / 4096u;

        if (numPages > 1024)
        {
            throw new NotSupportedException(
                "OTable exceeds PTDEPTH_1 capacity (4 MB / 1024 pages) — reduce count or add PTDEPTH_2."
            );
        }

        ulong[] dataPages = new ulong[numPages];
        for (uint i = 0; i < numPages; i++)
        {
            dataPages[i] = AllocPhysicalPage();
        }

        void* buffer = (void*)(HhdmOffset + dataPages[0]);
        MemoryOp.MemSet((byte*)buffer, 0, (int)alignedSize);

        MobFormat ptDepth;
        uint basePPN;

        if (numPages == 1)
        {
            ptDepth = MobFormat.PTDEPTH_0;
            basePPN = (uint)(dataPages[0] / 4096u);
        }
        else
        {
            ptDepth = MobFormat.PTDEPTH_1;

            ulong ptPage = AllocPhysicalPage();
            uint* pageTable = (uint*)(HhdmOffset + ptPage);
            for (uint i = 0; i < numPages; i++)
            {
                pageTable[i] = (uint)(dataPages[i] / 4096u);
            }

            basePPN = (uint)(ptPage / 4096u);
        }

        var cmd = (SVGA3dCmdSetOTableBase*)_driver3D.ReserveFIFO3D(
            (uint)FIFOCommand.SVGA_3D_CMD_SET_OTABLE_BASE, (uint)sizeof(SVGA3dCmdSetOTableBase)
        );

        cmd->type = type;
        cmd->baseAddress = basePPN;
        cmd->sizeInBytes = sizeInBytes;
        cmd->validSizeInBytes = 0;
        cmd->ptDepth = ptDepth;

        uint nextBefore = _driver.GetFIFO(FIFO.NextCmd);
        _driver.CommitFIFOCommand();
        uint stopAfter = _driver.GetFIFO(FIFO.Stop);

        Serial.WriteString("[SetOTable] type="); Serial.WriteHex((uint)type);
        Serial.WriteString(" basePPN=0x"); Serial.WriteHex(basePPN);
        Serial.WriteString(" ptDepth="); Serial.WriteHex((uint)ptDepth);
        Serial.WriteString(" nextBefore="); Serial.WriteHex(nextBefore);
        Serial.WriteString(" stopAfter="); Serial.WriteHex(stopAfter);
        Serial.WriteString(stopAfter == nextBefore + (uint)sizeof(SVGA3dCmdHeader) + (uint)sizeof(SVGA3dCmdSetOTableBase) ? " CONSUMED\n" : " NOT-CONSUMED\n");
    }

    public static void SetupOTables(this VMWareSVGAII3D _driver3D, SvgaIIDriver _driver)
    {
        if (s_oTablesAllocated)
        {
            return;
        }

        Serial.WriteString("[SetupOTables] start\n");

        SetupOneOTable(_driver3D, _driver, OTableType.SVGA_OTABLE_MOB, entrySize: 16, count: 512);
        Serial.WriteString("[SetupOTables] MOB table done\n");

        SetupOneOTable(_driver3D, _driver, OTableType.SVGA_OTABLE_CONTEXT, entrySize: 8, count: 64);
        Serial.WriteString("[SetupOTables] CONTEXT table done\n");

        SetupOneOTable(_driver3D, _driver, OTableType.SVGA_OTABLE_SURFACE, entrySize: 64, count: 512);
        Serial.WriteString("[SetupOTables] SURFACE table done\n");

        SetupOneOTable(_driver3D, _driver, OTableType.SVGA_OTABLE_SHADER, entrySize: 16, count: 256);
        Serial.WriteString("[SetupOTables] SHADER table done\n");

        SetupOneOTable(_driver3D, _driver, OTableType.SVGA_OTABLE_SCREENTARGET, entrySize: 64, count: 8);
        Serial.WriteString("[SetupOTables] SCREENTARGET table done\n");

        if (_driver3D.CheckDXCached())
        {
            Serial.WriteString("[SetupOTables] DX cached=true, doing DXCONTEXT table\n");
            SetupOneOTable(_driver3D, _driver, OTableType.SVGA_OTABLE_DXCONTEXT, entrySize: 8, count: 64);
            Serial.WriteString("[SetupOTables] DXCONTEXT table done\n");
        }
        else
        {
            Serial.WriteString("[SetupOTables] DX cached=false, skipping DXCONTEXT\n");
        }

        Serial.WriteString("[SetupOTables] syncing...\n");
        _driver.WriteRegister(Register.Sync, 1);
        while (_driver.ReadRegister(Register.Busy) != 0) { }
        Serial.WriteString("[SetupOTables] sync complete\n");

        s_oTablesAllocated = true;
}
}