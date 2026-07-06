// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory;
using SchedSpinLock = Cosmos.Kernel.Core.Scheduler.SpinLock;

namespace Cosmos.Kernel.HAL.Devices.Storage;

/// <summary>
/// SATA drive port driver. The bounce buffer is a single page allocated
/// from <see cref="PageAllocator"/>; the HBA DMAs to its physical address
/// while <see cref="ReadDataBlock16"/> / <see cref="WriteDataBlock8"/>
/// touch the kernel-virtual address.
///
/// <para>Error contract: command issue throws on failure (no free slot,
/// device timeout, task-file error), so <see cref="ReadBlock"/> /
/// <see cref="WriteBlock"/> never silently hand back stale bounce-buffer
/// contents. All I/O on a port is serialized by an internal lock — the
/// port owns a single bounce buffer.</para>
/// </summary>
public class Sata : BlockDevice
{
    private static int s_nextIndex;

    private readonly string _name;

    /// <inheritdoc />
    public override string Name => _name;

    public uint PortNumber => _portReg.PortNumber;

    private readonly PortRegisters _portReg;
    private readonly ulong _dataBufferVirt;
    private readonly ulong _dataBufferPhys;
    private const uint DataBlockSize = 512;

    /// <summary>
    /// Bounce-buffer capacity in sectors (one 4 KiB page). A command must
    /// never span more than this or the HBA would DMA past the page into
    /// adjacent kernel memory.
    /// </summary>
    private const uint MaxSectorsPerCommand = 4096 / DataBlockSize;

    /// <summary>
    /// Regular sector size (512 bytes).
    /// </summary>
    public const ulong RegularSectorSize = 512UL;

    // Serializes command issue + bounce-buffer access on this port: the
    // buffer is shared state, so an unserialized concurrent WriteBlock
    // would interleave another caller's data into an in-flight command.
    private SchedSpinLock _ioLock;

    // Properties
    public string SerialNo { get; }
    public string FirmwareRev { get; }
    public string ModelNo { get; }

    public unsafe Sata(PortRegisters portReg)
    {
        // Check if it is really a SATA Port!
        if (portReg.PortType != PortType.Sata || (portReg.CMD & (1U << 24)) != 0)
        {
            throw new Exception($" 0:{portReg.PortNumber} is not a SATA port!\n");
        }

        _portReg = portReg;

        // Unique per device instance ("sata0", "sata1", ...) so multi-disk
        // systems get distinguishable device and partition names. Built via
        // string.Concat rather than $"..." because the kernel runtime
        // doesn't link the interpolated-string handler.
        _name = "sata" + s_nextIndex++;

        // One page (4 KiB) of contiguous DMA-able memory, well above the 2-byte
        // alignment AHCI's PRDT.DBA requires. PageAllocator hands out the
        // kernel-virtual; VirtualToPhysical gets the address the HBA will see.
        // OOM at boot would have killed everything earlier, so we trust the
        // alloc; logging on the 32-bit-controller / high-physical edge case
        // (the HBA will then see a truncated DMA address) is enough.
        _dataBufferVirt = (ulong)PageAllocator.AllocPages(PageType.Unmanaged, 1, true);
        _dataBufferPhys = PageAllocator.VirtualToPhysical(_dataBufferVirt);

        if (!_portReg.Controller.Supports64BitAddressing && _dataBufferPhys > 0xFFFFFFFF)
        {
            Serial.WriteString("[SATA] WARNING: 32-bit-only controller with bounce buffer above 4 GiB at phys=0x");
            Serial.WriteHex(_dataBufferPhys);
            Serial.WriteString(" — DMA addressing will be truncated\n");
        }

        // Setting block size
        BlockSize = RegularSectorSize;

        // Send Identify command to get drive info
        SendSataCommand(AtaCommands.Identify);

        // Read identify data
        ushort[] xBuffer = new ushort[256];
        ReadDataBlock16(xBuffer);

        SerialNo = GetString(xBuffer, 10, 20);
        FirmwareRev = GetString(xBuffer, 23, 8);
        ModelNo = GetString(xBuffer, 27, 40);

        // Capacity: all I/O here is issued as READ/WRITE DMA EXT, so prefer
        // the 48-bit count in IDENTIFY words 100-103 (word 83 bit 10 = 48-bit
        // feature set supported). Words 60-61 saturate at 0x0FFFFFFF
        // (~128 GiB) and would silently truncate larger disks — misplacing
        // anything derived from BlockCount, like the backup GPT header.
        if ((xBuffer[83] & (1 << 10)) != 0)
        {
            BlockCount = (ulong)xBuffer[103] << 48 | (ulong)xBuffer[102] << 32
                       | (ulong)xBuffer[101] << 16 | xBuffer[100];
        }
        else
        {
            BlockCount = (uint)xBuffer[61] << 16 | xBuffer[60];
        }

        Serial.WriteString("[SATA] Initialized: ");
        Serial.WriteString(ModelNo.Trim());
        Serial.WriteString(" (");
        Serial.WriteNumber(BlockCount);
        Serial.WriteString(" sectors)\n");
    }

    /// <summary>
    /// Issue a non-LBA command (Identify, CacheFlushExt, …). Data-in
    /// commands (the Identify family) read up to one sector into the
    /// bounce buffer; everything else runs as a non-data command with an
    /// empty PRDT. Throws on failure.
    /// </summary>
    public void SendSataCommand(AtaCommands command)
    {
        bool hasData = command == AtaCommands.Identify
            || command == AtaCommands.IdentifyPacket
            || command == AtaCommands.IdentifyDma;
        _ioLock.Acquire();
        try
        {
            IssueCommandCore((byte)command, lba: 0, count: 0, isWrite: false, useLba48: false, hasData: hasData);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <summary>
    /// Issue an LBA48 read or write spanning <paramref name="count"/>
    /// sectors starting at <paramref name="start"/>. The transfer moves
    /// through the port's single-page bounce buffer, so
    /// <paramref name="count"/> is limited to
    /// <see cref="MaxSectorsPerCommand"/>. Throws on failure.
    /// </summary>
    public void SendSata48Command(AtaCommands command, ulong start, uint count)
    {
        if (count == 0 || count > MaxSectorsPerCommand)
        {
            throw new ArgumentOutOfRangeException(nameof(count),
                "SATA transfers are limited to the single-page bounce buffer.");
        }

        bool isWrite = command == AtaCommands.WriteDmaExt || command == AtaCommands.WriteDma;
        _ioLock.Acquire();
        try
        {
            IssueCommandCore((byte)command, start, count, isWrite, useLba48: true, hasData: true);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <summary>
    /// Build the command-list / FIS / PRDT for one in-flight command, ring
    /// the doorbell, and spin (bounded) until the slot drops. Caller must
    /// hold <see cref="_ioLock"/>. Throws on any failure — a silent return
    /// would let the caller consume stale bounce-buffer data as if the
    /// command had completed.
    /// </summary>
    private void IssueCommandCore(byte command, ulong lba, uint count, bool isWrite, bool useLba48, bool hasData)
    {
        // PxIS and PxSERR are RW1C: write all ones so stale error bits
        // (including TFES, bit 30) latched by a previous command can't
        // poison this one. Writing a partial mask (or zero) clears nothing.
        _portReg.IS = 0xFFFFFFFFu;
        _portReg.SERR = 0xFFFFFFFFu;

        int slot = FindCMDSlot();
        if (slot == -1)
        {
            throw new InvalidOperationException("SATA: no free command slot.");
        }

        ulong clbVirt = _portReg.Controller.PortCommandListVirt(PortNumber);
        ulong ctbaPhys = _portReg.Controller.PortCommandTablePhys(PortNumber, (uint)slot);
        ulong ctbaVirt = _portReg.Controller.PortCommandTableVirt(PortNumber, (uint)slot);

        HbaCommandHeader cmdHeader = new(clbVirt, (uint)slot)
        {
            CFL = 5,
            PRDTL = hasData ? (ushort)1 : (ushort)0,
            Write = (byte)(isWrite ? 1 : 0),
            CTBA = (uint)(ctbaPhys & 0xFFFFFFFF),
            CTBAU = (uint)(ctbaPhys >> 32)
        };

        HbaCommandTable cmdTable = new(ctbaVirt, cmdHeader.PRDTL);
        if (hasData)
        {
            cmdTable.PRDTEntry[0].DBA = (uint)(_dataBufferPhys & 0xFFFFFFFF);
            cmdTable.PRDTEntry[0].DBAU = (uint)(_dataBufferPhys >> 32);
            cmdTable.PRDTEntry[0].DBC = useLba48 ? count * DataBlockSize - 1 : DataBlockSize - 1;
            cmdTable.PRDTEntry[0].InterruptOnCompletion = 1;
        }

        FisRegisterH2D cmdFIS = new(cmdTable.CFIS)
        {
            FisType = (byte)FisType.RegisterH2D,
            IsCommand = 1,
            Command = command,
            Device = useLba48 ? (byte)(1 << 6) : (byte)0
        };
        if (useLba48)
        {
            cmdFIS.LBA0 = (byte)((lba >> 0) & 0xFF);
            cmdFIS.LBA1 = (byte)((lba >> 8) & 0xFF);
            cmdFIS.LBA2 = (byte)((lba >> 16) & 0xFF);
            cmdFIS.LBA3 = (byte)((lba >> 24) & 0xFF);
            cmdFIS.LBA4 = (byte)((lba >> 32) & 0xFF);
            cmdFIS.LBA5 = (byte)((lba >> 40) & 0xFF);
            cmdFIS.CountL = (byte)(count & 0xFF);
            cmdFIS.CountH = (byte)((count >> 8) & 0xFF);
        }

        int spin = 0;
        while ((_portReg.TFD & 0x88) != 0 && spin < 1000000)
        {
            spin++;
        }
        if (spin == 1000000)
        {
            throw new InvalidOperationException("SATA: port stuck busy (TFD BSY/DRQ) before command issue.");
        }

        // Order the command header/table/FIS/bounce-buffer stores (Normal
        // memory) before the doorbell store (Device memory): ARM64 does not
        // order the two on its own, so the HBA could otherwise fetch a
        // half-written command.
        PlatformHAL.Initializer?.DmaBarrier();

        // Ring the doorbell for the slot the command was actually built in —
        // PxCI is one bit per slot.
        _portReg.CI = 1U << slot;

        uint waitSpin = 0;
        while (true)
        {
            if ((_portReg.CI & (1U << slot)) == 0)
            {
                break;
            }
            if ((_portReg.IS & (1U << 30)) != 0)
            {
                throw new Exception("SATA Fatal error: Command aborted");
            }
            if (++waitSpin > 50_000_000)
            {
                // Bounded like the TFD wait above: a wedged device (link
                // drop, hot removal) must not spin the kernel forever.
                throw new InvalidOperationException("SATA: command completion timeout.");
            }
        }

        // Read barrier: the CI-clear observation is the completion signal;
        // the DMA'd bounce-buffer contents the caller consumes next must
        // not be read ahead of it on weakly-ordered ARM64.
        PlatformHAL.Initializer?.DmaBarrier();
    }

    /// <summary>
    /// Reset the port. Returns false when the command engine refused to
    /// stop — the port must then be left untouched (reprogramming CLB/FB
    /// on a running engine lets the HBA fetch garbage command headers and
    /// issue arbitrary DMA), not fixed up with a whole-HBA reset that
    /// would wipe every sibling port.
    /// </summary>
    public static bool PortReset(PortRegisters port)
    {
        // Stop the port command engine, then wait for CMD.ST to actually clear.
        port.CMD &= ~(1U << 0);
        for (int i = 0; i <= 50; i++)
        {
            if ((port.CMD & (1 << 0)) == 0)
            {
                break;
            }
            Ahci.Wait(10000);
        }
        if ((port.CMD & (1U << 0)) != 0)
        {
            Serial.WriteString("[SATA] Port engine refused to stop; leaving port offline\n");
            return false;
        }

        // COMRESET: set SCTL.DET=1 while preserving the SPD/IPM restriction
        // fields, hold it, then clear DET so the PHY retrains.
        port.SCTL = (port.SCTL & ~0xFU) | 1U;
        Ahci.Wait(1000);
        port.SCTL &= ~0xFU;

        // Wait (bounded) for the PHY to report an established link (DET == 3); a
        // missing or wedged device must not spin the boot CPU forever.
        int spin = 0;
        while ((port.SSTS & 0x0F) != 3 && spin < 1000000)
        {
            spin++;
        }

        // RW1C: clear every latched error bit, not just DIAG bit 0.
        port.SERR = 0xFFFFFFFFu;

        // Wait (bounded) for the COMRESET request bit to clear.
        spin = 0;
        while ((port.SCTL & 0x0F) != 0 && spin < 1000000)
        {
            spin++;
        }

        return true;
    }

    private int FindCMDSlot()
    {
        // If not set in SACT and CI, the slot is free
        uint slots = _portReg.SACT | _portReg.CI;

        for (int i = 0; i < 32; i++)
        {
            if ((slots & 1) == 0)
            {
                return i;
            }
            slots >>= 1;
        }

        return -1;
    }

    private string GetString(ushort[] buffer, int indexStart, int stringLength)
    {
        char[] chars = new char[stringLength];
        for (int i = 0; i < stringLength / 2; i++)
        {
            ushort xChar = buffer[indexStart + i];
            chars[i * 2] = (char)(xChar >> 8);
            chars[i * 2 + 1] = (char)xChar;
        }
        return new string(chars);
    }

    private unsafe void ReadDataBlock16(ushort[] buffer)
    {
        ushort* ptr = (ushort*)_dataBufferVirt;
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] = ptr[i];
        }
    }

    private unsafe void ReadDataBlock8(Span<byte> data)
    {
        byte* ptr = (byte*)_dataBufferVirt;
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = ptr[i];
        }
    }

    private unsafe void WriteDataBlock8(ReadOnlySpan<byte> data)
    {
        byte* ptr = (byte*)_dataBufferVirt;
        for (int i = 0; i < data.Length; i++)
        {
            ptr[i] = data[i];
        }
    }

    // BlockDevice implementation. The bounce buffer (_dataBufferVirt) is a
    // single page, so I/O is issued one sector per command; the port lock
    // keeps each command + bounce-buffer copy atomic against other threads.
    /// <inheritdoc />
    public override void ReadBlock(ulong blockNo, ulong blockCount, Span<byte> data)
    {
        int sector = (int)BlockSize;
        _ioLock.Acquire();
        try
        {
            for (ulong i = 0; i < blockCount; i++)
            {
                IssueCommandCore((byte)AtaCommands.ReadDmaExt, blockNo + i, 1, isWrite: false, useLba48: true, hasData: true);
                ReadDataBlock8(data.Slice((int)i * sector, sector));
            }
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <inheritdoc />
    public override void WriteBlock(ulong blockNo, ulong blockCount, ReadOnlySpan<byte> data)
    {
        int sector = (int)BlockSize;
        _ioLock.Acquire();
        try
        {
            for (ulong i = 0; i < blockCount; i++)
            {
                WriteDataBlock8(data.Slice((int)i * sector, sector));
                IssueCommandCore((byte)AtaCommands.WriteDmaExt, blockNo + i, 1, isWrite: true, useLba48: true, hasData: true);
            }
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Issues FLUSH CACHE EXT so completed writes reach stable media.
    /// WriteBlock deliberately does not flush per write — durability is the
    /// caller's policy via <see cref="Flush"/>, matching the shared
    /// IBlockDevice contract (and NVMe's behavior).
    /// </remarks>
    public override void Flush()
    {
        _ioLock.Acquire();
        try
        {
            IssueCommandCore((byte)AtaCommands.CacheFlushExt, lba: 0, count: 0, isWrite: false, useLba48: false, hasData: false);
        }
        finally
        {
            _ioLock.Release();
        }
    }
}
