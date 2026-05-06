// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core;

namespace Cosmos.Kernel.HAL.X64.Devices.Storage;

/// <summary>
/// AHCI Generic Host Control Registers.
/// </summary>
public class GenericRegisters
{
    private readonly ulong _address;

    public GenericRegisters(ulong address)
    {
        _address = address;
    }

    public uint Capabilities
    {
        get => Native.MMIO.Read32(_address + 0x00);
        set => Native.MMIO.Write32(_address + 0x00, value);
    }

    public uint GlobalHostControl
    {
        get => Native.MMIO.Read32(_address + 0x04);
        set => Native.MMIO.Write32(_address + 0x04, value);
    }

    public uint InterruptStatus
    {
        get => Native.MMIO.Read32(_address + 0x08);
        set => Native.MMIO.Write32(_address + 0x08, value);
    }

    public uint ImplementedPorts
    {
        get => Native.MMIO.Read32(_address + 0x0C);
        set => Native.MMIO.Write32(_address + 0x0C, value);
    }

    public uint AHCIVersion
    {
        get => Native.MMIO.Read32(_address + 0x10);
        set => Native.MMIO.Write32(_address + 0x10, value);
    }

    public uint CCC_Control
    {
        get => Native.MMIO.Read32(_address + 0x14);
        set => Native.MMIO.Write32(_address + 0x14, value);
    }

    public uint CCC_Ports
    {
        get => Native.MMIO.Read32(_address + 0x18);
        set => Native.MMIO.Write32(_address + 0x18, value);
    }

    public uint EM_Location
    {
        get => Native.MMIO.Read32(_address + 0x1C);
        set => Native.MMIO.Write32(_address + 0x1C, value);
    }

    public uint EM_Control
    {
        get => Native.MMIO.Read32(_address + 0x20);
        set => Native.MMIO.Write32(_address + 0x20, value);
    }

    public uint ExtendedCapabilities
    {
        get => Native.MMIO.Read32(_address + 0x24);
        set => Native.MMIO.Write32(_address + 0x24, value);
    }

    public uint BIOSHandOffStatus
    {
        get => Native.MMIO.Read32(_address + 0x28);
        set => Native.MMIO.Write32(_address + 0x28, value);
    }
}

/// <summary>
/// AHCI Port Registers.
/// </summary>
public class PortRegisters
{
    private readonly ulong _address;
    public uint PortNumber { get; }
    public PortType PortType { get; set; } = PortType.Nothing;
    public bool Active { get; set; }

    public PortRegisters(ulong baseAddress, uint portNumber)
    {
        PortNumber = portNumber;
        _address = baseAddress + 0x80 * portNumber;
        Active = false;
    }

    /// <summary>Command List Base Address</summary>
    public uint CLB
    {
        get => Native.MMIO.Read32(_address + 0x00);
        set => Native.MMIO.Write32(_address + 0x00, value);
    }

    /// <summary>Command List Base Address Upper</summary>
    public uint CLBU
    {
        get => Native.MMIO.Read32(_address + 0x04);
        set => Native.MMIO.Write32(_address + 0x04, value);
    }

    /// <summary>FIS Base Address</summary>
    public uint FB
    {
        get => Native.MMIO.Read32(_address + 0x08);
        set => Native.MMIO.Write32(_address + 0x08, value);
    }

    /// <summary>FIS Base Address Upper</summary>
    public uint FBU
    {
        get => Native.MMIO.Read32(_address + 0x0C);
        set => Native.MMIO.Write32(_address + 0x0C, value);
    }

    /// <summary>Interrupt Status</summary>
    public uint IS
    {
        get => Native.MMIO.Read32(_address + 0x10);
        set => Native.MMIO.Write32(_address + 0x10, value);
    }

    /// <summary>Interrupt Enable</summary>
    public uint IE
    {
        get => Native.MMIO.Read32(_address + 0x14);
        set => Native.MMIO.Write32(_address + 0x14, value);
    }

    /// <summary>Command</summary>
    public uint CMD
    {
        get => Native.MMIO.Read32(_address + 0x18);
        set => Native.MMIO.Write32(_address + 0x18, value);
    }

    /// <summary>Task File Data</summary>
    public uint TFD
    {
        get => Native.MMIO.Read32(_address + 0x20);
        set => Native.MMIO.Write32(_address + 0x20, value);
    }

    /// <summary>Signature</summary>
    public uint SIG
    {
        get => Native.MMIO.Read32(_address + 0x24);
        set => Native.MMIO.Write32(_address + 0x24, value);
    }

    /// <summary>SATA Status</summary>
    public uint SSTS
    {
        get => Native.MMIO.Read32(_address + 0x28);
        set => Native.MMIO.Write32(_address + 0x28, value);
    }

    /// <summary>SATA Control</summary>
    public uint SCTL
    {
        get => Native.MMIO.Read32(_address + 0x2C);
        set => Native.MMIO.Write32(_address + 0x2C, value);
    }

    /// <summary>SATA Error</summary>
    public uint SERR
    {
        get => Native.MMIO.Read32(_address + 0x30);
        set => Native.MMIO.Write32(_address + 0x30, value);
    }

    /// <summary>SATA Active</summary>
    public uint SACT
    {
        get => Native.MMIO.Read32(_address + 0x34);
        set => Native.MMIO.Write32(_address + 0x34, value);
    }

    /// <summary>Command Issue</summary>
    public uint CI
    {
        get => Native.MMIO.Read32(_address + 0x38);
        set => Native.MMIO.Write32(_address + 0x38, value);
    }

    /// <summary>SATA Notification</summary>
    public uint SNTF
    {
        get => Native.MMIO.Read32(_address + 0x3C);
        set => Native.MMIO.Write32(_address + 0x3C, value);
    }

    /// <summary>FIS-Based Switching</summary>
    public uint FBS
    {
        get => Native.MMIO.Read32(_address + 0x40);
        set => Native.MMIO.Write32(_address + 0x40, value);
    }

    /// <summary>Device Sleep</summary>
    public uint DEVSLP
    {
        get => Native.MMIO.Read32(_address + 0x44);
        set => Native.MMIO.Write32(_address + 0x44, value);
    }
}

/// <summary>
/// HBA Command Header structure.
/// </summary>
public class HBACommandHeader
{
    private readonly ulong _address;

    public HBACommandHeader(uint baseAddress, uint slot)
    {
        _address = baseAddress + 32 * slot;
        // Clear the header
        for (int i = 0; i < 8; i++)
        {
            Native.MMIO.Write32(_address + (ulong)(i * 4), 0);
        }
    }

    public byte CFL
    {
        get => (byte)(Native.MMIO.Read8(_address) & 0x1F);
        set => Native.MMIO.Write8(_address, value);
    }

    public byte ATAPI
    {
        get => (byte)((Native.MMIO.Read8(_address) >> 5) & 1);
        set
        {
            byte current = Native.MMIO.Read8(_address);
            Native.MMIO.Write8(_address, (byte)(current | (value << 5)));
        }
    }

    public byte Write
    {
        get => (byte)((Native.MMIO.Read8(_address) >> 6) & 1);
        set
        {
            byte current = Native.MMIO.Read8(_address);
            Native.MMIO.Write8(_address, (byte)(current | (value << 6)));
        }
    }

    public ushort PRDTL
    {
        get => Native.MMIO.Read16(_address + 0x02);
        set => Native.MMIO.Write16(_address + 0x02, value);
    }

    public uint PRDBC
    {
        get => Native.MMIO.Read32(_address + 0x04);
        set => Native.MMIO.Write32(_address + 0x04, value);
    }

    public uint CTBA
    {
        get => Native.MMIO.Read32(_address + 0x08);
        set => Native.MMIO.Write32(_address + 0x08, value);
    }

    public uint CTBAU
    {
        get => Native.MMIO.Read32(_address + 0x0C);
        set => Native.MMIO.Write32(_address + 0x0C, value);
    }
}

/// <summary>
/// HBA Command Table.
/// </summary>
public class HBACommandTable
{
    public uint CFIS { get; }
    public HBAPRDTEntry[] PRDTEntry { get; }

    public HBACommandTable(uint address, uint prdtCount)
    {
        CFIS = address;

        // Clear the first 0x80 bytes
        for (int i = 0; i < 0x80 / 4; i++)
        {
            Native.MMIO.Write32(address + (ulong)(i * 4), 0);
        }

        PRDTEntry = new HBAPRDTEntry[prdtCount];
        for (uint i = 0; i < prdtCount; i++)
        {
            PRDTEntry[i] = new HBAPRDTEntry(address + 0x80, i);
        }
    }

    public uint ACMD => CFIS + 0x40;
}

/// <summary>
/// HBA PRDT Entry.
/// </summary>
public class HBAPRDTEntry
{
    private readonly ulong _address;

    public HBAPRDTEntry(uint baseAddress, uint entry)
    {
        _address = baseAddress + 0x10 * entry;
        // Clear the entry
        for (int i = 0; i < 4; i++)
        {
            Native.MMIO.Write32(_address + (ulong)(i * 4), 0);
        }
    }

    public uint DBA
    {
        get => Native.MMIO.Read32(_address + 0x00);
        set => Native.MMIO.Write32(_address + 0x00, value);
    }

    public uint DBAU
    {
        get => Native.MMIO.Read32(_address + 0x04);
        set => Native.MMIO.Write32(_address + 0x04, value);
    }

    public uint DBC
    {
        get => Native.MMIO.Read32(_address + 0x0C) & 0x3FFFFF;
        set => Native.MMIO.Write32(_address + 0x0C, value);
    }

    public byte InterruptOnCompletion
    {
        get => (byte)(Native.MMIO.Read8(_address + 0x0F) >> 7);
        set => Native.MMIO.Write8(_address + 0x0F, (byte)(value << 7));
    }
}

/// <summary>
/// FIS Register Host to Device.
/// </summary>
public class FISRegisterH2D
{
    private readonly ulong _address;

    public FISRegisterH2D(uint address)
    {
        _address = address;
        // Clear the FIS (20 bytes)
        for (int i = 0; i < 5; i++)
        {
            Native.MMIO.Write32(_address + (ulong)(i * 4), 0);
        }
    }

    public byte FISType
    {
        get => Native.MMIO.Read8(_address + 0x00);
        set => Native.MMIO.Write8(_address + 0x00, value);
    }

    public byte IsCommand
    {
        get => (byte)(Native.MMIO.Read8(_address + 0x01) >> 7);
        set => Native.MMIO.Write8(_address + 0x01, (byte)(value << 7));
    }

    public byte Command
    {
        get => Native.MMIO.Read8(_address + 0x02);
        set => Native.MMIO.Write8(_address + 0x02, value);
    }

    public byte FeatureLow
    {
        get => Native.MMIO.Read8(_address + 0x03);
        set => Native.MMIO.Write8(_address + 0x03, value);
    }

    public byte LBA0
    {
        get => Native.MMIO.Read8(_address + 0x04);
        set => Native.MMIO.Write8(_address + 0x04, value);
    }

    public byte LBA1
    {
        get => Native.MMIO.Read8(_address + 0x05);
        set => Native.MMIO.Write8(_address + 0x05, value);
    }

    public byte LBA2
    {
        get => Native.MMIO.Read8(_address + 0x06);
        set => Native.MMIO.Write8(_address + 0x06, value);
    }

    public byte Device
    {
        get => Native.MMIO.Read8(_address + 0x07);
        set => Native.MMIO.Write8(_address + 0x07, value);
    }

    public byte LBA3
    {
        get => Native.MMIO.Read8(_address + 0x08);
        set => Native.MMIO.Write8(_address + 0x08, value);
    }

    public byte LBA4
    {
        get => Native.MMIO.Read8(_address + 0x09);
        set => Native.MMIO.Write8(_address + 0x09, value);
    }

    public byte LBA5
    {
        get => Native.MMIO.Read8(_address + 0x0A);
        set => Native.MMIO.Write8(_address + 0x0A, value);
    }

    public byte FeatureHigh
    {
        get => Native.MMIO.Read8(_address + 0x0B);
        set => Native.MMIO.Write8(_address + 0x0B, value);
    }

    public byte CountL
    {
        get => Native.MMIO.Read8(_address + 0x0C);
        set => Native.MMIO.Write8(_address + 0x0C, value);
    }

    public byte CountH
    {
        get => Native.MMIO.Read8(_address + 0x0D);
        set => Native.MMIO.Write8(_address + 0x0D, value);
    }

    public byte ICC
    {
        get => Native.MMIO.Read8(_address + 0x0E);
        set => Native.MMIO.Write8(_address + 0x0E, value);
    }

    public byte Control
    {
        get => Native.MMIO.Read8(_address + 0x0F);
        set => Native.MMIO.Write8(_address + 0x0F, value);
    }
}