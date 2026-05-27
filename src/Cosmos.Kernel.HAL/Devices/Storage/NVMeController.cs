// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Boot.Limine;
using Cosmos.Kernel.Core;
using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.Core.Scheduler;
using Cosmos.Kernel.HAL.Pci;
using SchedMutex = Cosmos.Kernel.Core.Scheduler.Mutex;
using SchedSpinLock = Cosmos.Kernel.Core.Scheduler.SpinLock;
using SchedThread = Cosmos.Kernel.Core.Scheduler.Thread;

namespace Cosmos.Kernel.HAL.Devices.Storage;

/// <summary>
/// One initialized NVMe controller. Owns its admin and a single IO
/// queue pair, and the namespaces it discovered. The admin queue is
/// polled (it only runs once at init); the I/O queue is interrupt-driven
/// via MSI-X so callers of <see cref="Read"/> / <see cref="Write"/> can
/// yield instead of burning a CPU on a spinloop, and up to
/// <see cref="IoQueueDepth"/> commands can be in flight concurrently
/// because every slot owns its own 4 KiB DMA bounce buffer.
///
/// <para>If MSI-X is unavailable (e.g. ARM64 today, or a pathological
/// PCI device with no MSI-X capability) the controller falls back to
/// polled completion, but in that mode commands are serialized through
/// an internal mutex.</para>
///
/// Limitations:
/// <list type="bullet">
/// <item>One IO submission and one IO completion queue (qid=1), depth 8.</item>
/// <item>Single-PRP transfers — caller never asks for more than one LBA
/// per command, so PRP2 is always 0.</item>
/// </list>
/// </summary>
public unsafe class NVMeController
{
    private const uint AdminQueueDepth = 8;
    private const uint IoQueueDepth = 8;
    private const uint IoQueueId = 1;
    private const ulong PageSize = 4096;

    /// <summary>
    /// Per-CID bookkeeping for in-flight I/O commands. Each slot carries
    /// its own bounce buffer so multiple namespaces (or threads) can hold
    /// independent in-flight commands without trampling one shared page.
    /// </summary>
    private sealed class IoSlot
    {
        public InterruptEvent Done = new();
        public uint Status;
        public bool InUse;
        public ulong DmaBufferVirt;
        public ulong DmaBufferPhys;
    }

    private readonly PciDevice _pci;
    private readonly NVMeRegisters _regs;

    // Admin queue
    private ulong _adminSqVirt;
    private ulong _adminCqVirt;
    private ulong _adminSqPhys;
    private ulong _adminCqPhys;
    private uint _adminSqTail;
    private uint _adminCqHead;
    private bool _adminCqPhase;
    private ushort _adminCmdId;

    // IO queue
    private ulong _ioSqVirt;
    private ulong _ioCqVirt;
    private ulong _ioSqPhys;
    private ulong _ioCqPhys;
    private uint _ioSqTail;
    private uint _ioCqHead;
    private bool _ioCqPhase;

    // I/O completion plumbing (MSI-X driven when possible).
    private MsiXContext _msiX;
    private bool _msiXEnabled;
    private IoSlot[]? _ioSlots;

    // Slot allocation. SpinLock guards both the slot in-use bits and the
    // _slotWaiters queue. SubmitSqLock guards the SQ tail + doorbell
    // critical section so concurrent submits don't clobber _ioSqTail.
    private SchedSpinLock _slotLock;
    private SchedSpinLock _submitSqLock;
    private readonly List<SchedThread> _slotWaiters = [];

    // Polled-completion fallback path: serializes the entire submit/wait
    // sequence so concurrent callers don't race on _ioCqHead / _ioCqPhase.
    // Unused once MSI-X is enabled.
    private readonly SchedMutex _polledIoMutex = new();

    public List<NVMeNamespace> Namespaces { get; } = new();

    /// <summary>
    /// True once I/O completions are delivered via MSI-X; false when the
    /// controller fell back to polled I/O (no MSI-X capability, or no platform
    /// MSI routing backend — e.g. an ARM64 GIC with no ITS). Lets a test assert
    /// which interrupt path a given QEMU profile actually exercised rather than
    /// just that I/O works.
    /// </summary>
    public bool IsMsiXEnabled => _msiXEnabled;

    public NVMeController(PciDevice pci)
    {
        _pci = pci;
        pci.EnableBusMaster(true);
        pci.EnableMemory(true);

        if (pci.BaseAddressBar == null || pci.BaseAddressBar.Length < 1)
        {
            throw new Exception("[NVMe] Invalid BAR configuration");
        }

        ulong bar0Phys = pci.GetBar64Address(0);
        if (bar0Phys == 0)
        {
            throw new Exception("[NVMe] BAR0 is not a memory BAR");
        }

        // ARM64 needs the BAR page installed as Device memory in TTBR1
        // before the HHDM virtual is dereferenceable for MMIO. No-op on x64.
        PlatformHAL.Initializer?.EnsureMmioMapped(bar0Phys);

        ulong hhdmOffset = Limine.HHDM.Response != null ? Limine.HHDM.Response->Offset : 0;
        ulong bar0Virt = bar0Phys + hhdmOffset;
        _regs = new NVMeRegisters(bar0Virt);

        Serial.WriteString("[NVMe] BAR0 phys=0x");
        Serial.WriteHex(bar0Phys);
        Serial.WriteString(" virt=0x");
        Serial.WriteHex(bar0Virt);
        Serial.WriteString("\n");
    }

    /// <summary>
    /// Bring the controller up: reset, set up admin queues, identify
    /// controller and namespaces, create one IO queue pair, register
    /// each namespace as a BlockDevice.
    /// </summary>
    public void Initialize()
    {
        DisableController();
        SetupAdminQueues();
        EnableController();
        Serial.WriteString("[NVMe] Controller ready\n");

        DiscoverNamespaces();
        SetupIoCompletionPlumbing();
        CreateIoQueues();
    }

    /// <summary>
    /// Allocate per-slot DMA buffers + InterruptEvent, enable MSI-X on
    /// the device, allocate an interrupt vector, and program the MSI-X
    /// table to deliver to it. Falls back to polled mode if the device
    /// has no MSI-X cap or the platform has no MSI routing backend.
    /// </summary>
    private void SetupIoCompletionPlumbing()
    {
        _ioSlots = new IoSlot[IoQueueDepth];
        for (int i = 0; i < IoQueueDepth; i++)
        {
            IoSlot slot = new();
            slot.DmaBufferVirt = (ulong)PageAllocator.AllocPages(PageType.Unmanaged, 1, true);
            slot.DmaBufferPhys = PageAllocator.VirtualToPhysical(slot.DmaBufferVirt);
            _ioSlots[i] = slot;
        }

        MsiXContext? ctx = MsiX.Enable(_pci);
        if (ctx == null)
        {
            Serial.WriteString("[NVMe] MSI-X unavailable, falling back to polled I/O\n");
            return;
        }

        _msiX = ctx.Value;
        // The binder allocates the underlying vector / LPI itself (x64 IDT
        // vector or ARM64 LPI INTID) and wires it to OnIoCompletion.
        MsiX.SetEntry(_msiX, 0, OnIoCompletion);
        _msiXEnabled = true;

        Serial.WriteString("[NVMe] I/O CQ -> MSI-X entry 0\n");
    }

    private void DisableController()
    {
        uint cc = _regs.CC;
        if ((cc & 1) != 0)
        {
            _regs.CC = cc & ~1u;
        }

        uint spin = 0;
        while ((_regs.CSTS & 1) != 0)
        {
            if (++spin > 1_000_000)
            {
                throw new Exception("[NVMe] Timeout waiting for CSTS.RDY=0");
            }
        }
    }

    private void EnableController()
    {
        // CC.IOSQES=6 (64-byte SQE), CC.IOCQES=4 (16-byte CQE), CC.MPS=0 (4K), CC.CSS=0, EN=1
        uint cc = (6u << 16) | (4u << 20) | 1u;
        _regs.CC = cc;

        uint spin = 0;
        while ((_regs.CSTS & 1) == 0)
        {
            if (++spin > 1_000_000)
            {
                throw new Exception("[NVMe] Timeout waiting for CSTS.RDY=1");
            }
        }
    }

    private void SetupAdminQueues()
    {
        _adminSqVirt = (ulong)PageAllocator.AllocPages(PageType.Unmanaged, 1, true);
        _adminCqVirt = (ulong)PageAllocator.AllocPages(PageType.Unmanaged, 1, true);
        _adminSqPhys = PageAllocator.VirtualToPhysical(_adminSqVirt);
        _adminCqPhys = PageAllocator.VirtualToPhysical(_adminCqVirt);
        _adminSqTail = 0;
        _adminCqHead = 0;
        _adminCqPhase = true;

        // AQA: ACQS in [27:16], ASQS in [11:0], both 0-based.
        _regs.AQA = ((AdminQueueDepth - 1) << 16) | (AdminQueueDepth - 1);
        _regs.ASQ = _adminSqPhys;
        _regs.ACQ = _adminCqPhys;
    }

    /// <summary>
    /// Submit an admin command and poll for its completion.
    /// Returns the CQE status code (0 = success).
    /// </summary>
    private uint SubmitAdmin(byte opcode, uint nsid, ulong prp1, uint cdw10, uint cdw11, uint cdw12)
    {
        ushort cid = _adminCmdId++;

        NVMeSqe sqe = new(_adminSqVirt + (ulong)_adminSqTail * 64);
        sqe.SetOpcode(opcode, cid);
        sqe.SetNsid(nsid);
        sqe.SetPrp1(prp1);
        sqe.SetPrp2(0);
        sqe.SetCdw10(cdw10);
        sqe.SetCdw11(cdw11);
        sqe.SetCdw12(cdw12);

        _adminSqTail = (_adminSqTail + 1) % AdminQueueDepth;
        Native.MMIO.Write32(_regs.SubmissionDoorbell(0), _adminSqTail);

        return WaitCompletion(_adminCqVirt, ref _adminCqHead, ref _adminCqPhase, AdminQueueDepth, qid: 0, expectCid: cid);
    }

    /// <summary>
    /// Read <paramref name="dst"/>.Length bytes (must equal block size *
    /// (numLogicalBlocksMinusOne+1)) starting at LBA <paramref name="lba"/>
    /// from namespace <paramref name="nsid"/>. Thread-safe — multiple
    /// callers run on independent slots up to <see cref="IoQueueDepth"/>.
    /// </summary>
    public uint Read(uint nsid, ulong lba, Span<byte> dst, ushort numLogicalBlocksMinusOne)
    {
        int slotIndex = AcquireSlot();
        if (slotIndex < 0)
        {
            throw new InvalidOperationException("[NVMe] no scheduler context for I/O");
        }
        try
        {
            IoSlot slot = _ioSlots![slotIndex];
            uint sc = SubmitOnSlot(NVMeIoOp.Read, nsid, slotIndex, lba, numLogicalBlocksMinusOne);
            if (sc == 0)
            {
                CopyOut(slot.DmaBufferVirt, dst);
            }
            return sc;
        }
        finally
        {
            ReleaseSlot(slotIndex);
        }
    }

    /// <summary>
    /// Write <paramref name="src"/> to namespace <paramref name="nsid"/>
    /// starting at LBA <paramref name="lba"/>. Thread-safe.
    /// </summary>
    public uint Write(uint nsid, ulong lba, ReadOnlySpan<byte> src, ushort numLogicalBlocksMinusOne)
    {
        int slotIndex = AcquireSlot();
        if (slotIndex < 0)
        {
            throw new InvalidOperationException("[NVMe] no scheduler context for I/O");
        }
        try
        {
            IoSlot slot = _ioSlots![slotIndex];
            CopyIn(src, slot.DmaBufferVirt);
            return SubmitOnSlot(NVMeIoOp.Write, nsid, slotIndex, lba, numLogicalBlocksMinusOne);
        }
        finally
        {
            ReleaseSlot(slotIndex);
        }
    }

    /// <summary>Flush volatile write cache for namespace <paramref name="nsid"/>.</summary>
    public uint Flush(uint nsid)
    {
        int slotIndex = AcquireSlot();
        if (slotIndex < 0)
        {
            throw new InvalidOperationException("[NVMe] no scheduler context for I/O");
        }
        try
        {
            return SubmitOnSlot(NVMeIoOp.Flush, nsid, slotIndex, 0, 0);
        }
        finally
        {
            ReleaseSlot(slotIndex);
        }
    }

    /// <summary>
    /// Find a free slot, mark it in-use, and return its index. If every
    /// slot is in flight, blocks the caller on the slot waiter queue
    /// until <see cref="ReleaseSlot"/> wakes one.
    /// </summary>
    private int AcquireSlot()
    {
        SchedThread? current = SchedulerManager.GetCpuState(SchedulerManager.GetCurrentCpuId()).CurrentThread;
        if (current == null)
        {
            return -1;
        }

        while (true)
        {
            _slotLock.Acquire();
            for (int i = 0; i < _ioSlots!.Length; i++)
            {
                if (!_ioSlots[i].InUse)
                {
                    _ioSlots[i].InUse = true;
                    _slotLock.Release();
                    return i;
                }
            }

            if (!_slotWaiters.Contains(current))
            {
                _slotWaiters.Add(current);
            }
            _slotLock.Release();

            SchedulerManager.BlockThread(current.CpuId, current);
            InternalCpu.Halt();
        }
    }

    private void ReleaseSlot(int index)
    {
        _slotLock.Acquire();
        _ioSlots![index].InUse = false;
        if (_slotWaiters.Count > 0)
        {
            SchedThread waiter = _slotWaiters[0];
            _slotWaiters.RemoveAt(0);
            _slotLock.Release();
            SchedulerManager.ReadyThread(waiter.CpuId, waiter);
            return;
        }
        _slotLock.Release();
    }

    /// <summary>
    /// Build the SQE for an already-acquired slot, ring the SQ doorbell,
    /// and wait for the matching CQE. Returns the device's status code
    /// (0 = success). Thread-safe.
    /// </summary>
    private uint SubmitOnSlot(byte opcode, uint nsid, int slotIndex, ulong lba, ushort numLogicalBlocksMinusOne)
    {
        IoSlot slot = _ioSlots![slotIndex];
        slot.Status = 0;
        ushort cid = (ushort)slotIndex;

        if (_msiXEnabled)
        {
            // Submit under the SQ lock, then wait outside it so other
            // threads can enqueue while this one is parked.
            _submitSqLock.Acquire();
            try
            {
                NVMeSqe sqe = new(_ioSqVirt + (ulong)_ioSqTail * 64);
                sqe.SetOpcode(opcode, cid);
                sqe.SetNsid(nsid);
                sqe.SetPrp1(slot.DmaBufferPhys);
                sqe.SetPrp2(0);
                sqe.SetCdw10((uint)(lba & 0xFFFFFFFF));
                sqe.SetCdw11((uint)(lba >> 32));
                sqe.SetCdw12(numLogicalBlocksMinusOne);

                _ioSqTail = (_ioSqTail + 1) % IoQueueDepth;
                Native.MMIO.Write32(_regs.SubmissionDoorbell(IoQueueId), _ioSqTail);
            }
            finally
            {
                _submitSqLock.Release();
            }

            slot.Done.Wait();
            return slot.Status;
        }

        // Polled fallback: the CQ head/phase aren't safe to share, so
        // serialize the whole submit/wait sequence.
        _polledIoMutex.Acquire();
        try
        {
            NVMeSqe sqe = new(_ioSqVirt + (ulong)_ioSqTail * 64);
            sqe.SetOpcode(opcode, cid);
            sqe.SetNsid(nsid);
            // TEMP VALIDATION BUG (revert): offset the PRP by one block on the
            // polled path ONLY, so every polled NVMe transfer DMAs to/from the
            // wrong place and the round-trip data mismatches. The MSI-X branch
            // above is untouched. Proves the matrix catches a config-specific
            // bug: x64 nvme (MSI-X) stays green, arm64 nvme+gicv2 (polled) goes
            // red, for the same driver and the same test.
            sqe.SetPrp1(slot.DmaBufferPhys + 512);
            sqe.SetPrp2(0);
            sqe.SetCdw10((uint)(lba & 0xFFFFFFFF));
            sqe.SetCdw11((uint)(lba >> 32));
            sqe.SetCdw12(numLogicalBlocksMinusOne);

            _ioSqTail = (_ioSqTail + 1) % IoQueueDepth;
            Native.MMIO.Write32(_regs.SubmissionDoorbell(IoQueueId), _ioSqTail);

            slot.Status = WaitCompletion(_ioCqVirt, ref _ioCqHead, ref _ioCqPhase, IoQueueDepth, qid: IoQueueId, expectCid: cid);
            return slot.Status;
        }
        finally
        {
            _polledIoMutex.Release();
        }
    }

    /// <summary>
    /// MSI-X handler for the I/O completion queue. Drains every CQE
    /// whose phase matches the expected phase, advances the CQ head,
    /// rings the doorbell, and signals each slot's
    /// <see cref="InterruptEvent"/>. Performs no allocation and no
    /// interface dispatch (per the project's ISR-safety rules).
    /// </summary>
    private void OnIoCompletion(ref IRQContext context)
    {
        if (_ioSlots == null || _ioCqVirt == 0)
        {
            return;
        }

        bool drained = false;
        while (true)
        {
            NVMeCqe cqe = new(_ioCqVirt + (ulong)_ioCqHead * 16);
            if (cqe.Phase != _ioCqPhase)
            {
                break;
            }

            ushort cid = cqe.CommandIdentifier;
            uint sc = cqe.StatusCode;

            _ioCqHead = (_ioCqHead + 1) % IoQueueDepth;
            if (_ioCqHead == 0)
            {
                _ioCqPhase = !_ioCqPhase;
            }
            drained = true;

            if (cid < _ioSlots.Length)
            {
                IoSlot slot = _ioSlots[cid];
                slot.Status = sc;
                slot.Done.Signal();
            }
        }

        if (drained)
        {
            Native.MMIO.Write32(_regs.CompletionDoorbell(IoQueueId), _ioCqHead);
        }
    }

    private uint WaitCompletion(ulong cqBase, ref uint head, ref bool expectedPhase, uint depth, uint qid, ushort expectCid)
    {
        uint spin = 0;
        while (true)
        {
            NVMeCqe cqe = new(cqBase + (ulong)head * 16);
            if (cqe.Phase == expectedPhase)
            {
                uint sc = cqe.StatusCode;
                if (cqe.CommandIdentifier != expectCid)
                {
                    Serial.WriteString("[NVMe] Warning: CQE CID mismatch (got ");
                    Serial.WriteNumber(cqe.CommandIdentifier);
                    Serial.WriteString(", expected ");
                    Serial.WriteNumber(expectCid);
                    Serial.WriteString(")\n");
                }

                head = (head + 1) % depth;
                if (head == 0)
                {
                    expectedPhase = !expectedPhase;
                }
                Native.MMIO.Write32(_regs.CompletionDoorbell(qid), head);
                return sc;
            }

            if (++spin > 50_000_000)
            {
                throw new Exception("[NVMe] Timeout waiting for command completion");
            }
        }
    }

    private void DiscoverNamespaces()
    {
        ulong identifyVirt = (ulong)PageAllocator.AllocPages(PageType.Unmanaged, 1, true);
        ulong identifyPhys = PageAllocator.VirtualToPhysical(identifyVirt);

        try
        {
            // Identify Controller (CNS=0x01) — only used to confirm the controller is responsive.
            uint sc = SubmitAdmin(NVMeAdminOp.Identify, nsid: 0, prp1: identifyPhys, cdw10: NVMeCns.Controller, cdw11: 0, cdw12: 0);
            if (sc != 0)
            {
                Serial.WriteString("[NVMe] Identify Controller failed, status=0x");
                Serial.WriteHex(sc);
                Serial.WriteString("\n");
                return;
            }

            // Identify Active Namespace List (CNS=0x02) — returns up to 1024 NSIDs.
            ZeroPage(identifyVirt);
            sc = SubmitAdmin(NVMeAdminOp.Identify, nsid: 0, prp1: identifyPhys, cdw10: NVMeCns.ActiveNamespaceList, cdw11: 0, cdw12: 0);
            if (sc != 0)
            {
                Serial.WriteString("[NVMe] Identify Active NS List failed, status=0x");
                Serial.WriteHex(sc);
                Serial.WriteString("\n");
                return;
            }

            // Snapshot the namespace list before reusing the buffer for
            // per-namespace identifies — RegisterNamespace overwrites the
            // page, so we cannot iterate nsidList in place.
            uint* nsidList = (uint*)identifyVirt;
            Span<uint> nsids = stackalloc uint[1024];
            int nsCount = 0;
            for (int i = 0; i < 1024; i++)
            {
                uint nsid = nsidList[i];
                if (nsid == 0)
                {
                    break;
                }
                nsids[nsCount++] = nsid;
            }

            for (int i = 0; i < nsCount; i++)
            {
                RegisterNamespace(nsids[i], identifyVirt, identifyPhys);
            }
        }
        finally
        {
            PageAllocator.Free((void*)identifyVirt);
        }
    }

    private void RegisterNamespace(uint nsid, ulong identifyVirt, ulong identifyPhys)
    {
        ZeroPage(identifyVirt);
        uint sc = SubmitAdmin(NVMeAdminOp.Identify, nsid: nsid, prp1: identifyPhys, cdw10: NVMeCns.Namespace, cdw11: 0, cdw12: 0);
        if (sc != 0)
        {
            Serial.WriteString("[NVMe] Identify Namespace nsid=");
            Serial.WriteNumber(nsid);
            Serial.WriteString(" failed, status=0x");
            Serial.WriteHex(sc);
            Serial.WriteString("\n");
            return;
        }

        // NSZE (8 bytes, offset 0): namespace size in logical blocks.
        ulong nsze = *(ulong*)identifyVirt;
        if (nsze == 0)
        {
            return;
        }
        // FLBAS (1 byte, offset 26): bits [3:0] are the active LBA Format index.
        byte flbas = *(byte*)(identifyVirt + 26);
        int lbafIndex = flbas & 0x0F;
        // LBAF entries start at offset 128, each 4 bytes; LBADS is byte 2 of each.
        byte lbads = *(byte*)(identifyVirt + 128 + (ulong)(lbafIndex * 4) + 2);
        ulong blockSize = 1UL << lbads;

        Serial.WriteString("[NVMe] Namespace nsid=");
        Serial.WriteNumber(nsid);
        Serial.WriteString(" blocks=");
        Serial.WriteNumber(nsze);
        Serial.WriteString(" blockSize=");
        Serial.WriteNumber(blockSize);
        Serial.WriteString("\n");

        Namespaces.Add(new NVMeNamespace(this, nsid, nsze, blockSize));
    }

    private void CreateIoQueues()
    {
        _ioSqVirt = (ulong)PageAllocator.AllocPages(PageType.Unmanaged, 1, true);
        _ioCqVirt = (ulong)PageAllocator.AllocPages(PageType.Unmanaged, 1, true);
        _ioSqPhys = PageAllocator.VirtualToPhysical(_ioSqVirt);
        _ioCqPhys = PageAllocator.VirtualToPhysical(_ioCqVirt);
        _ioSqTail = 0;
        _ioCqHead = 0;
        _ioCqPhase = true;

        // Create IO Completion Queue first — Create IO SQ refers to it.
        // CDW10: bits [31:16] = qsize-1, bits [15:0] = qid
        // CDW11: bits [31:16] = IV (interrupt vector), bit 1 = IEN, bit 0 = PC
        uint cqCdw10 = ((IoQueueDepth - 1) << 16) | IoQueueId;
        uint cqCdw11 = _msiXEnabled ? ((0u << 16) | (1u << 1) | 1u) : 1u;
        uint sc = SubmitAdmin(NVMeAdminOp.CreateIoCq, nsid: 0, prp1: _ioCqPhys, cdw10: cqCdw10, cdw11: cqCdw11, cdw12: 0);
        if (sc != 0)
        {
            throw new Exception("[NVMe] Create IO CQ failed");
        }

        // Create IO Submission Queue. CDW11: bit 0 = PC, bits [2:1] = QPRIO (0=urgent),
        // bits [31:16] = CQID.
        uint sqCdw10 = ((IoQueueDepth - 1) << 16) | IoQueueId;
        uint sqCdw11 = (IoQueueId << 16) | 1; // CQID=1, PC=1
        sc = SubmitAdmin(NVMeAdminOp.CreateIoSq, nsid: 0, prp1: _ioSqPhys, cdw10: sqCdw10, cdw11: sqCdw11, cdw12: 0);
        if (sc != 0)
        {
            throw new Exception("[NVMe] Create IO SQ failed");
        }

        Serial.WriteString("[NVMe] IO queue pair created (qid=1, depth=");
        Serial.WriteNumber(IoQueueDepth);
        Serial.WriteString(")\n");
    }

    private static void ZeroPage(ulong virtAddr)
    {
        ulong* p = (ulong*)virtAddr;
        for (int i = 0; i < (int)(PageSize / 8); i++)
        {
            p[i] = 0;
        }
    }

    private static void CopyIn(ReadOnlySpan<byte> src, ulong dstVirt)
    {
        byte* dst = (byte*)dstVirt;
        for (int i = 0; i < src.Length; i++)
        {
            dst[i] = src[i];
        }
    }

    private static void CopyOut(ulong srcVirt, Span<byte> dst)
    {
        byte* src = (byte*)srcVirt;
        for (int i = 0; i < dst.Length; i++)
        {
            dst[i] = src[i];
        }
    }
}
