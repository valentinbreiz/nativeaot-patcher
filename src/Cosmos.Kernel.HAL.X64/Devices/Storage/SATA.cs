// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.HAL.BlockDevice;

namespace Cosmos.Kernel.HAL.X64.Devices.Storage;

/// <summary>
/// SATA drive port driver.
/// </summary>
public class SATA : StoragePort
{
    public override PortType PortType => PortType.SATA;
    public override string PortName => "SATA";
    public override uint PortNumber => _portReg.PortNumber;

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
        SendSATA28Command(0, 0, 0);

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
    /// Send a SATA command.
    /// </summary>
    public void SendSATACommand(ATACommands command)
    {
        _portReg.IS = 0xFFFF;

        int slot = FindCMDSlot();
        if (slot == -1)
        {
            return;
        }

        var cmdHeader = new HBACommandHeader(_portReg.CLB, (uint)slot);
        cmdHeader.CFL = 5;
        cmdHeader.PRDTL = 1;
        cmdHeader.Write = 0;

        cmdHeader.CTBA = (uint)((uint)(AHCIBase.AHCI + 0xA000) + 0x2000 * PortNumber + 0x100 * slot);

        var cmdTable = new HBACommandTable(cmdHeader.CTBA, cmdHeader.PRDTL);

        cmdTable.PRDTEntry[0].DBA = _dataBlockBase;
        cmdTable.PRDTEntry[0].DBC = 511;
        cmdTable.PRDTEntry[0].InterruptOnCompletion = 1;

        var cmdFIS = new FISRegisterH2D(cmdTable.CFIS)
        {
            FISType = (byte)FISType.FIS_Type_RegisterH2D,
            IsCommand = 1,
            Command = (byte)command,
            Device = 0
        };

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
    /// Send a 28-bit SATA command.
    /// </summary>
    public void SendSATA28Command(ATACommands command, uint start, uint count)
    {
        bool isIdentify = (start == 0 && count == 0);

        _portReg.IS = 0xFFFF;

        int slot = FindCMDSlot();
        if (slot == -1)
        {
            return;
        }

        var cmdHeader = new HBACommandHeader(_portReg.CLB, (uint)slot);
        cmdHeader.CFL = 5;
        cmdHeader.PRDTL = 1;
        cmdHeader.Write = 0;

        cmdHeader.CTBA = (uint)((uint)(AHCIBase.AHCI + 0xA000) + 0x2000 * PortNumber + 0x100 * slot);

        var cmdTable = new HBACommandTable(cmdHeader.CTBA, cmdHeader.PRDTL);

        // Last entry
        if (isIdentify)
        {
            cmdTable.PRDTEntry[cmdHeader.PRDTL - 1].DBA = _dataBlockBase;
            cmdTable.PRDTEntry[cmdHeader.PRDTL - 1].DBC = 511;
            cmdTable.PRDTEntry[cmdHeader.PRDTL - 1].InterruptOnCompletion = 1;
        }
        else
        {
            cmdTable.PRDTEntry[cmdHeader.PRDTL - 1].DBA = _dataBlockBase;
            cmdTable.PRDTEntry[cmdHeader.PRDTL - 1].DBC = count * 512 - 1;
            cmdTable.PRDTEntry[cmdHeader.PRDTL - 1].InterruptOnCompletion = 1;
        }

        if (isIdentify)
        {
            var cmdFIS = new FISRegisterH2D(cmdTable.CFIS)
            {
                FISType = (byte)FISType.FIS_Type_RegisterH2D,
                IsCommand = 1,
                Command = (byte)ATACommands.Identify,
                Device = 0
            };
        }
        else
        {
            var cmdFIS = new FISRegisterH2D(cmdTable.CFIS)
            {
                FISType = (byte)FISType.FIS_Type_RegisterH2D,
                IsCommand = 1,
                Command = (byte)command,
                LBA0 = (byte)(start & 0xFF),
                LBA1 = (byte)((start >> 8) & 0xFF),
                LBA2 = (byte)((start >> 16) & 0xFF),
                Device = (byte)(0x40 | ((start >> 24) & 0x0F)),
                CountL = (byte)(count & 0xFF)
            };
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
    /// Send a 48-bit SATA command.
    /// </summary>
    public void SendSATA48Command(ATACommands command, ulong start, uint count)
    {
        _portReg.IS = 0xFFFF;

        int slot = FindCMDSlot();
        if (slot == -1)
        {
            return;
        }

        // Determine if this is a write command
        bool isWrite = command == ATACommands.WriteDmaExt || command == ATACommands.WriteDma;

        var cmdHeader = new HBACommandHeader(_portReg.CLB, (uint)slot);
        cmdHeader.CFL = 5;
        cmdHeader.PRDTL = 1;
        cmdHeader.Write = (byte)(isWrite ? 1 : 0);

        cmdHeader.CTBA = (uint)((uint)(AHCIBase.AHCI + 0xA000) + 0x2000 * PortNumber + 0x100 * slot);

        var cmdTable = new HBACommandTable(cmdHeader.CTBA, cmdHeader.PRDTL);

        // Last entry
        cmdTable.PRDTEntry[cmdHeader.PRDTL - 1].DBA = _dataBlockBase;
        cmdTable.PRDTEntry[cmdHeader.PRDTL - 1].DBC = count * 512 - 1;
        cmdTable.PRDTEntry[cmdHeader.PRDTL - 1].InterruptOnCompletion = 1;

        var cmdFIS = new FISRegisterH2D(cmdTable.CFIS)
        {
            FISType = (byte)FISType.FIS_Type_RegisterH2D,
            IsCommand = 1,
            Command = (byte)command,
            LBA0 = (byte)((start >> 00) & 0xFF),
            LBA1 = (byte)((start >> 08) & 0xFF),
            LBA2 = (byte)((start >> 16) & 0xFF),
            LBA3 = (byte)((start >> 24) & 0xFF),
            LBA4 = (byte)((start >> 32) & 0xFF),
            LBA5 = (byte)((start >> 40) & 0xFF),
            Device = 1 << 6,
            CountL = (byte)(count & 0xFF),
            CountH = (byte)((count >> 8) & 0xFF)
        };

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

    // BaseBlockDevice implementation
    public override void ReadBlock(ulong blockNo, ulong blockCount, Span<byte> data)
    {
        SendSATA48Command(ATACommands.ReadDmaExt, blockNo, (uint)blockCount);
        ReadDataBlock8(data);
    }

    public override void WriteBlock(ulong blockNo, ulong blockCount, Span<byte> data)
    {
        Serial.WriteString("[SATA] WriteBlock LBA=");
        Serial.WriteNumber(blockNo);
        Serial.WriteString(" Count=");
        Serial.WriteNumber(blockCount);
        Serial.WriteString("\n");

        Serial.WriteString("[SATA] Copying data to DMA buffer...\n");
        WriteDataBlock8(data);

        Serial.WriteString("[SATA] Sending WriteDmaExt command...\n");
        SendSATA48Command(ATACommands.WriteDmaExt, blockNo, (uint)blockCount);

        Serial.WriteString("[SATA] Sending CacheFlush...\n");
        SendSATACommand(ATACommands.CacheFlush);

        Serial.WriteString("[SATA] WriteBlock done\n");
    }
}
