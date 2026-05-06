// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Boot.Limine;
using Cosmos.Kernel.Core;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.HAL.Pci;

namespace Cosmos.Kernel.HAL.X64.Devices.Storage;

/// <summary>
/// One initialized NVMe controller. Owns its admin and a single IO
/// queue pair, and the namespaces it discovered. Submission is polled —
/// matching the SATA driver's discipline; no interrupts.
///
/// Limitations:
/// <list type="bullet">
/// <item>QEMU NVMe BAR0 fits in 32 bits, so only the lower BAR is consumed.
/// Real-hardware NVMe with BAR0 above 4 GiB would need a 64-bit BAR
/// helper on <see cref="PciDeviceNormal"/>.</item>
/// <item>One IO submission and one IO completion queue (qid=1), depth 8.
/// Sufficient for serialized 4 KiB-and-under transfers.</item>
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
    private ushort _ioCmdId;

    /// <summary>Shared 4 KiB DMA bounce buffer used by all namespaces on this controller.</summary>
    public ulong DmaBufferVirt { get; private set; }
    public ulong DmaBufferPhys { get; private set; }

    public List<NVMeNamespace> Namespaces { get; } = new();

    public NVMeController(PciDevice pci)
    {
        pci.EnableBusMaster(true);
        pci.EnableMemory(true);

        if (pci.BaseAddressBar == null || pci.BaseAddressBar.Length < 1)
        {
            throw new Exception("[NVMe] Invalid BAR configuration");
        }

        ulong hhdmOffset = Limine.HHDM.Response != null ? Limine.HHDM.Response->Offset : 0;
        ulong bar0Phys = pci.BaseAddressBar[0].BaseAddress;
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

        // Pre-allocate the shared bounce buffer used for IO transfers.
        DmaBufferVirt = (ulong)PageAllocator.AllocPages(PageType.Unmanaged, 1, true);
        DmaBufferPhys = PageAllocator.VirtualToPhysical(DmaBufferVirt);

        DiscoverNamespaces();
        CreateIoQueues();
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
    /// Submit an IO command on the IO queue pair (qid=1) and poll for completion.
    /// </summary>
    public uint SubmitIo(byte opcode, uint nsid, ulong prp1, ulong slba, ushort numLogicalBlocksMinusOne)
    {
        ushort cid = _ioCmdId++;

        NVMeSqe sqe = new(_ioSqVirt + (ulong)_ioSqTail * 64);
        sqe.SetOpcode(opcode, cid);
        sqe.SetNsid(nsid);
        sqe.SetPrp1(prp1);
        sqe.SetPrp2(0);
        sqe.SetCdw10((uint)(slba & 0xFFFFFFFF));
        sqe.SetCdw11((uint)(slba >> 32));
        sqe.SetCdw12(numLogicalBlocksMinusOne);

        _ioSqTail = (_ioSqTail + 1) % IoQueueDepth;
        Native.MMIO.Write32(_regs.SubmissionDoorbell(IoQueueId), _ioSqTail);

        return WaitCompletion(_ioCqVirt, ref _ioCqHead, ref _ioCqPhase, IoQueueDepth, qid: IoQueueId, expectCid: cid);
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

        uint* nsidList = (uint*)identifyVirt;
        for (int i = 0; i < 1024; i++)
        {
            uint nsid = nsidList[i];
            if (nsid == 0)
            {
                break;
            }
            RegisterNamespace(nsid, identifyVirt, identifyPhys);
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
        // CDW11: bit 0 = PC (Physically Contiguous), bit 1 = IEN (0 = no interrupts)
        uint cqCdw10 = ((IoQueueDepth - 1) << 16) | IoQueueId;
        uint cqCdw11 = 1; // PC=1, IEN=0
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
}
