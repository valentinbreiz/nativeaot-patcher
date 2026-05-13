// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core;
using Cosmos.Kernel.Core.ARM64.Bridge;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory;
using SchedSpinLock = Cosmos.Kernel.Core.Scheduler.SpinLock;

namespace Cosmos.Kernel.Core.ARM64.Cpu;

/// <summary>
/// GICv3 Interrupt Translation Service (ITS) driver. The ITS is the
/// component that turns a PCI MSI write — a 32-bit EventID dropped into
/// <c>GITS_TRANSLATER</c> — into an LPI delivered to a target CPU's
/// redistributor. To do that it consults two in-memory tables it owns
/// (Device table and Collection table) plus a per-device Interrupt
/// Translation Table (ITT) whose layout the spec leaves opaque.
///
/// <para>This driver:</para>
/// <list type="bullet">
/// <item>Allocates the device and collection tables from <c>GITS_BASER</c>
/// configuration.</item>
/// <item>Allocates a 4 KiB command-queue ring and points <c>GITS_CBASER</c>
/// at it.</item>
/// <item>Issues one <c>MAPC</c> at init time mapping collection 0 to the
/// boot CPU's redistributor.</item>
/// <item>Exposes <see cref="MapDevice"/> (issues <c>MAPD</c>) and
/// <see cref="MapEvent"/> (issues <c>MAPTI</c>) for the MSI-X binder.</item>
/// </list>
///
/// Single-CPU only: every (DeviceID, EventID) is mapped to collection 0.
/// </summary>
public static unsafe class GICv3Its
{
    // Register offsets (frame 0 = control frame).
    private const uint GITS_CTLR = 0x0000;
    private const uint GITS_TYPER = 0x0008;
    private const uint GITS_CBASER = 0x0080;
    private const uint GITS_CWRITER = 0x0088;
    private const uint GITS_CREADR = 0x0090;
    private const uint GITS_BASER0 = 0x0100; // 8 entries, stride 8

    // Translation register frame is at +0x10000 from ITS base.
    private const uint GITS_TRANSLATER_OFF = 0x10040;

    private const uint GITS_CTLR_ENABLED = 1U << 0;
    private const uint GITS_CTLR_QUIESCENT = 1U << 31;

    private const ulong BASER_VALID = 1UL << 63;
    private const ulong BASER_INDIRECT = 1UL << 62;
    private const ulong BASER_INNERSHAREABLE = 1UL << 10;
    // [61:59] InnerCache: 5 = Normal Inner Read-Allocate Write-Allocate Write-Back.
    // Anything else (e.g. value 1 = Non-cacheable) makes the ITS bypass CPU
    // caches when reading these tables, which silently desyncs from our
    // (cacheable) writes — MAPD/MAPTI appear to complete but the device
    // entry the ITS actually fetches is whatever's still in DRAM.
    private const ulong BASER_INNER_CACHE_RaWaWb = 5UL << 59;
    private const ulong BASER_PAGE_SIZE_4K = 0UL << 8;

    private const ulong CBASER_VALID = 1UL << 63;
    private const ulong CBASER_INNERSHAREABLE = 1UL << 10;
    private const ulong CBASER_INNER_CACHE_RaWaWb = 5UL << 59;

    // Command opcodes (low byte of cmd[0]).
    private const byte CMD_MAPD = 0x08;
    private const byte CMD_MAPC = 0x09;
    private const byte CMD_MAPTI = 0x0A;
    private const byte CMD_INV = 0x0C;
    private const byte CMD_SYNC = 0x05;

    // Per-collection state.
    private const ushort BootCollectionId = 0;

    private static ulong _itsBase;          // virt = phys (TTBR0 identity)
    private static ulong _translaterPhys;   // GITS_TRANSLATER physical address; written by devices via MSI
    private static ulong _cmdQueueVirt;
    private static ulong _cmdQueuePhys;
    private static uint _cmdQueueSize;      // bytes
    private static ulong _cmdWriteOff;      // mirror of GITS_CWRITER
    private static int _ittEntrySize;
    private static bool _physicalTargetAddress;
    private static ulong _bootRedistTarget; // target value for collection 0 (phys addr or proc number<<16)
    private static SchedSpinLock _cmdLock;
    private static bool _initialized;

    public static bool IsInitialized => _initialized;

    /// <summary>
    /// The physical address devices write their <c>data</c> dword to when
    /// signalling an MSI. Becomes the MSI-X table entry's address field.
    /// </summary>
    public static ulong TranslaterPhysAddr => _translaterPhys;

    /// <summary>
    /// Initialize the ITS at the given physical base. Must be called after
    /// <see cref="GICv3Lpi.Initialize"/> on the boot CPU's redistributor.
    /// </summary>
    public static void Initialize(ulong itsBase, ulong bootRedistRdBase)
    {
        if (_initialized)
        {
            return;
        }

        _itsBase = itsBase;
        _translaterPhys = itsBase + GITS_TRANSLATER_OFF;

        // Make sure the ITS is disabled while we configure tables.
        uint ctlr = Native.MMIO.Read32(_itsBase + GITS_CTLR);
        if ((ctlr & GITS_CTLR_ENABLED) != 0)
        {
            Native.MMIO.Write32(_itsBase + GITS_CTLR, ctlr & ~GITS_CTLR_ENABLED);
            bool quiesced = false;
            for (int i = 0; i < 1_000_000; i++)
            {
                if ((Native.MMIO.Read32(_itsBase + GITS_CTLR) & GITS_CTLR_QUIESCENT) != 0)
                {
                    quiesced = true;
                    break;
                }
            }
            if (!quiesced)
            {
                throw new System.InvalidOperationException("[GICv3-ITS] timed out waiting for QUIESCENT after disable");
            }
        }

        ulong typer = Native.MMIO.Read64(_itsBase + GITS_TYPER);
        _ittEntrySize = (int)(((typer >> 4) & 0xF) + 1);
        _physicalTargetAddress = ((typer >> 19) & 0x1) != 0;

        // Compute target address for MAPC / SYNC.
        if (_physicalTargetAddress)
        {
            _bootRedistTarget = bootRedistRdBase;
        }
        else
        {
            // Processor_Number is GICR_TYPER bits [23:8]. We're sole CPU so 0
            // is almost always right, but read it for correctness.
            ulong rdTyper = Native.MMIO.Read64(bootRedistRdBase + 0x008 /* GICR_TYPER */);
            ulong procNum = (rdTyper >> 8) & 0xFFFF;
            _bootRedistTarget = procNum << 16;
        }

        // Configure GITS_BASER[i] for Device and Collection tables.
        for (int i = 0; i < 8; i++)
        {
            ulong off = GITS_BASER0 + (ulong)i * 8;
            ulong baser = Native.MMIO.Read64(_itsBase + (uint)off);
            byte type = (byte)((baser >> 56) & 0x7);
            if (type == 1 /* Device */)
            {
                ConfigureBaser(off, baser, type, pages: 8); // 32 KiB device table — covers thousands of devices
            }
            else if (type == 4 /* Collection */)
            {
                ConfigureBaser(off, baser, type, pages: 1); // 4 KiB — far more collections than we need
            }
            // Other types (vCPU, reserved) left untouched.
        }

        // Allocate command queue (one 4 KiB page, 256 commands of 32 bytes each).
        _cmdQueueSize = 4096;
        _cmdQueueVirt = (ulong)PageAllocator.AllocPages(PageType.Unmanaged, 1, zero: true);
        _cmdQueuePhys = PageAllocator.VirtualToPhysical(_cmdQueueVirt);

        ulong cbaser = CBASER_VALID
                     | CBASER_INNERSHAREABLE
                     | CBASER_INNER_CACHE_RaWaWb
                     | (_cmdQueuePhys & 0x000FFFFFFFFFF000UL)
                     | 0; // size = (4KB / 4KB) - 1 = 0
        Native.MMIO.Write64(_itsBase + GITS_CBASER, cbaser);
        Native.MMIO.Write64(_itsBase + GITS_CWRITER, 0);
        _cmdWriteOff = 0;

        // Enable ITS.
        Native.MMIO.Write32(_itsBase + GITS_CTLR, GITS_CTLR_ENABLED);

        // Map collection 0 -> boot CPU's redistributor.
        _initialized = true;
        EnqueueMapc(BootCollectionId, _bootRedistTarget, valid: true);
        EnqueueSync();
        FlushCommandQueue();

        Serial.WriteString("[GICv3-ITS] enabled at 0x");
        Serial.WriteHex(itsBase);
        Serial.WriteString("\n");
    }

    /// <summary>
    /// Allocate an ITT for <paramref name="deviceId"/> and issue MAPD.
    /// <paramref name="maxEvents"/> is rounded up to the next power of two
    /// (minimum 2, the ITS hardware lower bound).
    /// </summary>
    public static void MapDevice(uint deviceId, uint maxEvents)
    {
        if (!_initialized)
        {
            return;
        }

        uint nrEvents = maxEvents < 2 ? 2u : RoundUpPow2(maxEvents);
        int sizeField = Log2(nrEvents) - 1; // ARM IHI 0069G: size = log2(nr_ites) - 1
        if (sizeField < 0)
        {
            sizeField = 0;
        }

        ulong ittBytes = (ulong)nrEvents * (ulong)_ittEntrySize;
        // ITT must be 256-byte aligned. PageAllocator returns 4 KiB pages
        // which already satisfies that; allocate at least 1 page.
        ulong pages = (ittBytes + 4095) / 4096;
        if (pages == 0)
        {
            pages = 1;
        }
        ulong ittVirt = (ulong)PageAllocator.AllocPages(PageType.Unmanaged, (uint)pages, zero: true);
        ulong ittPhys = PageAllocator.VirtualToPhysical(ittVirt);

        EnqueueMapd(deviceId, ittPhys, sizeField, valid: true);
        EnqueueSync();
        FlushCommandQueue();
    }

    /// <summary>
    /// Wire (deviceId, eventId) → LPI INTID <paramref name="lpi"/> on the
    /// boot CPU's collection. The caller is expected to have allocated the
    /// LPI via <c>InterruptManager.AllocateLpi</c> and enabled it via
    /// <c>GICv3Lpi.EnableLpi</c>.
    /// </summary>
    public static void MapEvent(uint deviceId, uint eventId, uint lpi)
    {
        if (!_initialized)
        {
            return;
        }
        EnqueueMapti(deviceId, eventId, lpi, BootCollectionId);
        // INV invalidates the ITS's (and target redistributor's) cached
        // LPI configuration for (deviceId, eventId). Required: when
        // GICR_TYPER.DirectLPI == 0, GICR_INVLPIR is RES0, so writing the
        // prop-table byte alone never propagates — the only path that
        // refreshes the cache is an ITS INV / INVALL command.
        EnqueueInv(deviceId, eventId);
        EnqueueSync();
        FlushCommandQueue();
    }

    // ── BASER configuration ───────────────────────────────────────────

    private static void ConfigureBaser(ulong off, ulong oldBaser, byte type, uint pages)
    {
        int entrySize = (int)(((oldBaser >> 48) & 0x1F) + 1);

        ulong tableVirt = (ulong)PageAllocator.AllocPages(PageType.Unmanaged, pages, zero: true);
        ulong tablePhys = PageAllocator.VirtualToPhysical(tableVirt);

        ulong baser = ((ulong)type << 56)
                    | ((ulong)(entrySize - 1) << 48)
                    | (tablePhys & 0x0000FFFFFFFFF000UL)
                    | BASER_INNER_CACHE_RaWaWb
                    | BASER_INNERSHAREABLE
                    | BASER_PAGE_SIZE_4K
                    | (ulong)((pages - 1) & 0xFF)
                    | BASER_VALID;
        Native.MMIO.Write64(_itsBase + (uint)off, baser);

        // Read back; if shareability bits got cleared the GIC silently
        // refuses Inner Shareable, drop them.
        ulong rb = Native.MMIO.Read64(_itsBase + (uint)off);
        if ((rb & (3UL << 10)) == 0 && (baser & (3UL << 10)) != 0)
        {
            baser &= ~(3UL << 10);
            Native.MMIO.Write64(_itsBase + (uint)off, baser);
        }
    }

    // ── Command queue posting ─────────────────────────────────────────

    private static void EnqueueRaw(ulong cmd0, ulong cmd1, ulong cmd2, ulong cmd3)
    {
        // Caller holds _cmdLock.
        ulong* q = (ulong*)(_cmdQueueVirt + _cmdWriteOff);
        q[0] = cmd0;
        q[1] = cmd1;
        q[2] = cmd2;
        q[3] = cmd3;

        _cmdWriteOff += 32;
        if (_cmdWriteOff >= _cmdQueueSize)
        {
            _cmdWriteOff = 0;
        }
    }

    private static void EnqueueMapc(ushort collection, ulong target, bool valid)
    {
        _cmdLock.Acquire();
        // cmd[0]: opcode in low byte
        // cmd[2]: collection (15:0) | target_addr [47:16] << 16 (PTA mode) or proc_num<<16 (non-PTA)
        //         valid in bit 63
        ulong c2 = (ulong)collection
                 | (target & 0x0000FFFFFFFF0000UL)
                 | (valid ? (1UL << 63) : 0);
        EnqueueRaw(CMD_MAPC, 0, c2, 0);
        _cmdLock.Release();
    }

    private static void EnqueueMapd(uint deviceId, ulong ittPhys, int sizeField, bool valid)
    {
        _cmdLock.Acquire();
        ulong c0 = CMD_MAPD | ((ulong)deviceId << 32);
        ulong c1 = (ulong)(sizeField & 0x1F);
        ulong c2 = (ittPhys & 0x000FFFFFFFFFFF00UL) | (valid ? (1UL << 63) : 0);
        EnqueueRaw(c0, c1, c2, 0);
        _cmdLock.Release();
    }

    private static void EnqueueMapti(uint deviceId, uint eventId, uint lpi, ushort collection)
    {
        _cmdLock.Acquire();
        ulong c0 = CMD_MAPTI | ((ulong)deviceId << 32);
        ulong c1 = (ulong)eventId | ((ulong)lpi << 32);
        ulong c2 = (ulong)collection;
        EnqueueRaw(c0, c1, c2, 0);
        _cmdLock.Release();
    }

    private static void EnqueueInv(uint deviceId, uint eventId)
    {
        _cmdLock.Acquire();
        ulong c0 = CMD_INV | ((ulong)deviceId << 32);
        ulong c1 = (ulong)eventId;
        EnqueueRaw(c0, c1, 0, 0);
        _cmdLock.Release();
    }

    private static void EnqueueSync()
    {
        _cmdLock.Acquire();
        ulong c2 = _bootRedistTarget & 0x0000FFFFFFFF0000UL;
        EnqueueRaw(CMD_SYNC, 0, c2, 0);
        _cmdLock.Release();
    }

    /// <summary>
    /// Publish the new write offset and spin until the ITS has consumed
    /// every command we posted (CWRITER == CREADR).
    /// </summary>
    private static void FlushCommandQueue()
    {
        _cmdLock.Acquire();
        // DSB so the command bytes are globally visible BEFORE the ITS sees
        // the advanced CWRITER pointer; otherwise the ITS may fetch stale
        // queue contents and ignore our command.
        DeviceMapperNative.DsbIsb();
        Native.MMIO.Write64(_itsBase + GITS_CWRITER, _cmdWriteOff);

        for (int i = 0; i < 10_000_000; i++)
        {
            ulong reader = Native.MMIO.Read64(_itsBase + GITS_CREADR);
            if ((reader & ~0x1FUL) == _cmdWriteOff)
            {
                // Spec: GITS_CREADR.Stalled (bit 0) == 1 means the ITS
                // rejected the last command and won't advance further.
                // Treat that as fatal — the next MAPD/MAPTI would be issued
                // against an ITS in unknown state and devices would never
                // see their interrupts.
                if ((reader & 0x1) != 0)
                {
                    _cmdLock.Release();
                    throw new System.InvalidOperationException("[GICv3-ITS] command queue STALLED");
                }
                _cmdLock.Release();
                return;
            }
        }
        _cmdLock.Release();
        throw new System.InvalidOperationException("[GICv3-ITS] command queue flush timeout");
    }

    private static uint RoundUpPow2(uint v)
    {
        v--;
        v |= v >> 1;
        v |= v >> 2;
        v |= v >> 4;
        v |= v >> 8;
        v |= v >> 16;
        v++;
        return v == 0 ? 1u : v;
    }

    private static int Log2(uint v)
    {
        int r = 0;
        while ((v >>= 1) != 0)
        {
            r++;
        }
        return r;
    }
}
