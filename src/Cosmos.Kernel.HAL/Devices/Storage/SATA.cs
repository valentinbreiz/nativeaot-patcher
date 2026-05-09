// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory;

namespace Cosmos.Kernel.HAL.Devices.Storage;

/// <summary>
/// SATA drive port driver.
/// </summary>
public class SATA : BlockDevice
{
    public override string Name => "SATA";

    public uint PortNumber => _portReg.PortNumber;

    private readonly PortRegisters _portReg;
    private readonly uint _dataBlockBase;
    private const uint DataBlockSize = 512;

    /// <summary>
    /// Regular sector size (512 bytes).
    /// </summary>
    public const ulong RegularSectorSize = 512UL;

    // Properties
    public string SerialNo { get; }
    public string FirmwareRev { get; }
    public string ModelNo { get; }

    public unsafe SATA(PortRegisters portReg)
    {
        // Check if it is really a SATA Port!
        if (portReg.PortType != PortType.SATA || (portReg.CMD & (1U << 24)) != 0)
        {
            throw new Exception($" 0:{portReg.PortNumber} is not a SATA port!\n");
        }

        _portReg = portReg;

        // Allocate data block for DMA transfers
        _dataBlockBase = (uint)MemoryOp.Alloc(DataBlockSize);

        // Setting block size
        BlockSize = RegularSectorSize;

        // Send Identify command to get drive info
        SendSATACommand(ATACommands.Identify);

        // Read identify data
        ushort[] xBuffer = new ushort[256];
        ReadDataBlock16(xBuffer);

        SerialNo = GetString(xBuffer, 10, 20);
        FirmwareRev = GetString(xBuffer, 23, 8);
        ModelNo = GetString(xBuffer, 27, 40);

        BlockCount = ((uint)xBuffer[61] << 16 | xBuffer[60]) - 1;

        Serial.WriteString("[SATA] Initialized: ");
        Serial.WriteString(ModelNo.Trim());
        Serial.WriteString(" (");
        Serial.WriteNumber(BlockCount);
        Serial.WriteString(" sectors)\n");
    }

    /// <summary>
    /// Issue a non-LBA command (Identify, CacheFlush, …). Reads up to one
    /// sector into the bounce buffer.
    /// </summary>
    public void SendSATACommand(ATACommands command) =>
        IssueCommand((byte)command, lba: 0, count: 0, isWrite: false, useLba48: false);

    /// <summary>
    /// Issue an LBA48 read or write spanning <paramref name="count"/>
    /// sectors starting at <paramref name="start"/>.
    /// </summary>
    public void SendSATA48Command(ATACommands command, ulong start, uint count)
    {
        bool isWrite = command == ATACommands.WriteDmaExt || command == ATACommands.WriteDma;
        IssueCommand((byte)command, start, count, isWrite, useLba48: true);
    }

    /// <summary>
    /// Build the command-list / FIS / PRDT for one in-flight command, ring
    /// the doorbell, and spin until the slot drops. Caller picks the FIS
    /// shape via <paramref name="useLba48"/>:
    /// <list type="bullet">
    /// <item><c>false</c>: no LBA / no transfer count, PRDT sized for one
    /// sector. Used for Identify and CacheFlush.</item>
    /// <item><c>true</c>: 48-bit LBA + sector count, PRDT sized for
    /// <paramref name="count"/> sectors.</item>
    /// </list>
    /// </summary>
    private void IssueCommand(byte command, ulong lba, uint count, bool isWrite, bool useLba48)
    {
        _portReg.IS = 0xFFFF;

        int slot = FindCMDSlot();
        if (slot == -1)
        {
            return;
        }

        HBACommandHeader cmdHeader = new(_portReg.CLB, (uint)slot)
        {
            CFL = 5,
            PRDTL = 1,
            Write = (byte)(isWrite ? 1 : 0),
            CTBA = (uint)((uint)(AHCIBase.AHCI + 0xA000) + 0x2000 * PortNumber + 0x100 * slot)
        };

        HBACommandTable cmdTable = new(cmdHeader.CTBA, cmdHeader.PRDTL);
        cmdTable.PRDTEntry[0].DBA = _dataBlockBase;
        cmdTable.PRDTEntry[0].DBC = useLba48 ? count * 512 - 1 : 511;
        cmdTable.PRDTEntry[0].InterruptOnCompletion = 1;

        FISRegisterH2D cmdFIS = new(cmdTable.CFIS)
        {
            FISType = (byte)FISType.FIS_Type_RegisterH2D,
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
            Serial.WriteString("[SATA] Port timed out!\n");
            return;
        }

        _portReg.CI = 1U;

        while (true)
        {
            if ((_portReg.CI & (1 << slot)) == 0)
            {
                break;
            }
            if ((_portReg.IS & (1 << 30)) != 0)
            {
                throw new Exception("SATA Fatal error: Command aborted");
            }
        }
    }

    /// <summary>
    /// Reset the port.
    /// </summary>
    public static void PortReset(PortRegisters port)
    {
        // Semi-StopCMD()
        port.CMD &= ~(1U << 0);
        int i;
        for (i = 0; i <= 50; i++)
        {
            if ((port.CMD & (1 << 0)) == 0)
            {
                break;
            }
            AHCI.Wait(10000);
        }
        if (i == 101)
        {
            AHCI.HBAReset();
        }

        port.SCTL = 1;
        AHCI.Wait(1000);
        port.SCTL &= ~(1U << 0);

        while ((port.SSTS & 0x0F) != 3) { }

        port.SERR = 1;

        while ((port.SCTL & 0x0F) != 0) { }
    }

    private int FindCMDSlot()
    {
        // If not set in SACT and CI, the slot is free
        var slots = _portReg.SACT | _portReg.CI;

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
        var chars = new char[stringLength];
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
        ushort* ptr = (ushort*)_dataBlockBase;
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] = ptr[i];
        }
    }

    private unsafe void ReadDataBlock8(Span<byte> data)
    {
        byte* ptr = (byte*)_dataBlockBase;
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = ptr[i];
        }
    }

    private unsafe void WriteDataBlock8(Span<byte> data)
    {
        byte* ptr = (byte*)_dataBlockBase;
        for (int i = 0; i < data.Length; i++)
        {
            ptr[i] = data[i];
        }
    }

    // BlockDevice implementation. The bounce buffer (_dataBlockBase) is sized
    // for one sector, so we issue one DMA command per block rather than
    // overrunning the buffer on multi-block transfers.
    public override void ReadBlock(ulong blockNo, ulong blockCount, Span<byte> data)
    {
        int sector = (int)BlockSize;
        for (ulong i = 0; i < blockCount; i++)
        {
            SendSATA48Command(ATACommands.ReadDmaExt, blockNo + i, 1);
            ReadDataBlock8(data.Slice((int)i * sector, sector));
        }
    }

    public override void WriteBlock(ulong blockNo, ulong blockCount, Span<byte> data)
    {
        Serial.WriteString("[SATA] WriteBlock LBA=");
        Serial.WriteNumber(blockNo);
        Serial.WriteString(" Count=");
        Serial.WriteNumber(blockCount);
        Serial.WriteString("\n");

        int sector = (int)BlockSize;
        for (ulong i = 0; i < blockCount; i++)
        {
            WriteDataBlock8(data.Slice((int)i * sector, sector));
            SendSATA48Command(ATACommands.WriteDmaExt, blockNo + i, 1);
        }

        SendSATACommand(ATACommands.CacheFlush);
        Serial.WriteString("[SATA] WriteBlock done\n");
    }
}
