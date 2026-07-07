// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core;

namespace Cosmos.Kernel.HAL.Devices.Storage;

/// <summary>
/// NVMe controller register block accessor. Wraps the BAR0 virtual base
/// (BAR0 phys + HHDM offset) and exposes the spec registers via
/// <see cref="Native.MMIO"/>.
///
/// Layout follows NVM Express 1.4 §3.1.
/// </summary>
public class NvmeRegisters
{
    private readonly ulong _base;

    public NvmeRegisters(ulong baseVirtAddress)
    {
        _base = baseVirtAddress;
    }

    /// <summary>BAR0 virtual address (used by the doorbell helper).</summary>
    public ulong BaseAddress => _base;

    /// <summary>CAP — Controller Capabilities (RO, 64-bit, offset 0x00).</summary>
    public ulong CAP => Native.MMIO.Read64(_base + 0x00);

    /// <summary>VS — Version (RO, 32-bit, offset 0x08).</summary>
    public uint VS => Native.MMIO.Read32(_base + 0x08);

    /// <summary>INTMS — Interrupt Mask Set (RW, 32-bit, offset 0x0C).</summary>
    public uint INTMS
    {
        get => Native.MMIO.Read32(_base + 0x0C);
        set => Native.MMIO.Write32(_base + 0x0C, value);
    }

    /// <summary>INTMC — Interrupt Mask Clear (RW, 32-bit, offset 0x10).</summary>
    public uint INTMC
    {
        get => Native.MMIO.Read32(_base + 0x10);
        set => Native.MMIO.Write32(_base + 0x10, value);
    }

    /// <summary>CC — Controller Configuration (RW, 32-bit, offset 0x14).</summary>
    public uint CC
    {
        get => Native.MMIO.Read32(_base + 0x14);
        set => Native.MMIO.Write32(_base + 0x14, value);
    }

    /// <summary>CSTS — Controller Status (RO, 32-bit, offset 0x1C).</summary>
    public uint CSTS => Native.MMIO.Read32(_base + 0x1C);

    /// <summary>AQA — Admin Queue Attributes (RW, 32-bit, offset 0x24).</summary>
    public uint AQA
    {
        get => Native.MMIO.Read32(_base + 0x24);
        set => Native.MMIO.Write32(_base + 0x24, value);
    }

    /// <summary>ASQ — Admin Submission Queue Base (RW, 64-bit, offset 0x28).</summary>
    public ulong ASQ
    {
        get => Native.MMIO.Read64(_base + 0x28);
        set => Native.MMIO.Write64(_base + 0x28, value);
    }

    /// <summary>ACQ — Admin Completion Queue Base (RW, 64-bit, offset 0x30).</summary>
    public ulong ACQ
    {
        get => Native.MMIO.Read64(_base + 0x30);
        set => Native.MMIO.Write64(_base + 0x30, value);
    }

    /// <summary>CAP.MQES — Maximum Queue Entries Supported (1-based).</summary>
    public uint MQES => (uint)(CAP & 0xFFFF) + 1;

    /// <summary>CAP.DSTRD — Doorbell Stride (in dwords, log2). Stride bytes = 4 &lt;&lt; DSTRD.</summary>
    public uint DSTRD => (uint)((CAP >> 32) & 0xF);

    /// <summary>CAP.TO — worst-case CSTS.RDY transition time, in 500 ms units.</summary>
    public uint TO => (uint)((CAP >> 24) & 0xFF);

    /// <summary>
    /// Compute the byte address of submission-queue tail doorbell for
    /// queue id <paramref name="qid"/>. Doorbells live at BAR0 + 0x1000 +
    /// (2 * qid + 0) * (4 &lt;&lt; CAP.DSTRD).
    /// </summary>
    public ulong SubmissionDoorbell(uint qid)
    {
        ulong stride = 4UL << (int)DSTRD;
        return _base + 0x1000UL + (ulong)(2 * qid) * stride;
    }

    /// <summary>
    /// Compute the byte address of completion-queue head doorbell for
    /// queue id <paramref name="qid"/>.
    /// </summary>
    public ulong CompletionDoorbell(uint qid)
    {
        ulong stride = 4UL << (int)DSTRD;
        return _base + 0x1000UL + (ulong)(2 * qid + 1) * stride;
    }
}
