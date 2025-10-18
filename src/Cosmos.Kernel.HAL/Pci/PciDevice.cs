// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.HAL.Pci.Enums;

namespace Cosmos.Kernel.HAL.Pci;

public class PciDevice
{
    public readonly uint Bus;
    public readonly uint Slot;
    public readonly uint Function;

    public readonly uint Bar0;

    public readonly ushort VendorId;
    public readonly ushort DeviceId;

    public readonly ushort Status;

    public readonly byte RevisionId;
    public readonly byte ProgIf;
    public readonly byte Subclass;
    public readonly byte ClassCode;
    public readonly byte SecondaryBusNumber;

    public readonly bool DeviceExists;

    public readonly PciHeaderType HeaderType;
    public readonly PciBist Bist;
    public readonly PciInterruptPin InterruptPin;

    public const ushort ConfigAddressPort = 0xCF8;
    public const ushort ConfigDataPort = 0xCFC;

    public readonly PciBaseAddressBar[] BaseAddressBar;

    public byte InterruptLine { get; private set; }

    public PciCommand Command
    {
        get => (PciCommand)ReadRegister16(0x04);
        set => WriteRegister16(0x04, (ushort)value);
    }

    /// <summary>
    /// Has this device been claimed by a driver
    /// </summary>
    public bool Claimed { get; set; }

    public PciDevice(uint bus, uint slot, uint function)
    {
        Bus = bus;
        Slot = slot;
        Function = function;

        VendorId = ReadRegister16((byte)Config.VendorId);
        DeviceId = ReadRegister16((byte)Config.DeviceId);

        Bar0 = ReadRegister32((byte)Config.Bar0);

        //Command = ReadRegister16((byte)Config.Command);
        //Status = ReadRegister16((byte)Config.Status);

        RevisionId = ReadRegister8((byte)Config.RevisionId);
        ProgIf = ReadRegister8((byte)Config.ProgIf);
        Subclass = ReadRegister8((byte)Config.SubClass);
        ClassCode = ReadRegister8((byte)Config.Class);
        SecondaryBusNumber = ReadRegister8((byte)Config.SecondaryBusNo);

        HeaderType = (PciHeaderType)ReadRegister8((byte)Config.HeaderType);
        Bist = (PciBist)ReadRegister8((byte)Config.Bist);
        InterruptPin = (PciInterruptPin)ReadRegister8((byte)Config.InterruptPin);
        InterruptLine = ReadRegister8((byte)Config.InterruptLine);

        if ((uint)VendorId == 0xFF && (uint)DeviceId == 0xFFFF)
        {
            DeviceExists = false;
        }
        else
        {
            DeviceExists = true;
        }

        if (HeaderType == PciHeaderType.Normal)
        {
            BaseAddressBar = new PciBaseAddressBar[6];
            BaseAddressBar[0] = new PciBaseAddressBar(ReadRegister32(0x10));
            BaseAddressBar[1] = new PciBaseAddressBar(ReadRegister32(0x14));
            BaseAddressBar[2] = new PciBaseAddressBar(ReadRegister32(0x18));
            BaseAddressBar[3] = new PciBaseAddressBar(ReadRegister32(0x1C));
            BaseAddressBar[4] = new PciBaseAddressBar(ReadRegister32(0x20));
            BaseAddressBar[5] = new PciBaseAddressBar(ReadRegister32(0x24));
        }
    }

    public void EnableDevice() => Command |= PciCommand.Master | PciCommand.Io | PciCommand.Memory;

    /// <summary>
    /// Get header type.
    /// </summary>
    /// <param name="bus">A bus.</param>
    /// <param name="slot">A slot.</param>
    /// <param name="function">A function.</param>
    /// <returns>ushort value.</returns>
    public static ushort GetHeaderType(ushort bus, ushort slot, ushort function)
    {
        uint xAddr = GetAddressBase(bus, slot, function) | (0xE & 0xFC);
        PlatformHAL.PortIO.WriteDWord(ConfigAddressPort, xAddr);
        return (byte)((PlatformHAL.PortIO.ReadDWord(ConfigDataPort) >> (0xE % 4 * 8)) & 0xFF);
    }

    /// <summary>
    /// Get vendor ID.
    /// </summary>
    /// <param name="bus">A bus.</param>
    /// <param name="slot">A slot.</param>
    /// <param name="function">A function.</param>
    /// <returns>UInt16 value.</returns>
    public static ushort GetVendorId(ushort bus, ushort slot, ushort function)
    {
        uint xAddr = GetAddressBase(bus, slot, function) | (0x0 & 0xFC);
        PlatformHAL.PortIO.WriteDWord(ConfigAddressPort, xAddr);
        return (ushort)((PlatformHAL.PortIO.ReadDWord(ConfigDataPort) >> (0x0 % 4 * 8)) & 0xFFFF);
    }

    #region IOReadWrite

    /// <summary>
    /// Read register - 8-bit.
    /// </summary>
    /// <param name="aRegister">A register to read.</param>
    /// <returns>byte value.</returns>
    public byte ReadRegister8(byte aRegister)
    {
        uint xAddr = GetAddressBase(Bus, Slot, Function) | (uint)(aRegister & 0xFC);
        PlatformHAL.PortIO.WriteDWord(ConfigAddressPort, xAddr);
        return (byte)((PlatformHAL.PortIO.ReadDWord(ConfigDataPort) >> (aRegister % 4 * 8)) & 0xFF);
    }

    public void WriteRegister8(byte aRegister, byte value)
    {
        uint xAddr = GetAddressBase(Bus, Slot, Function) | (uint)(aRegister & 0xFC);
        PlatformHAL.PortIO.WriteDWord(ConfigAddressPort, xAddr);
        PlatformHAL.PortIO.WriteByte(ConfigDataPort, value);
    }

    /// <summary>
    /// Read register 16.
    /// </summary>
    /// <param name="aRegister">A register.</param>
    /// <returns>UInt16 value.</returns>
    public ushort ReadRegister16(byte aRegister)
    {
        uint xAddr = GetAddressBase(Bus, Slot, Function) | (uint)(aRegister & 0xFC);
        PlatformHAL.PortIO.WriteDWord(ConfigAddressPort, xAddr);
        return (ushort)((PlatformHAL.PortIO.ReadDWord(ConfigDataPort) >> (aRegister % 4 * 8)) & 0xFFFF);
    }

    /// <summary>
    /// Write register 16.
    /// </summary>
    /// <param name="aRegister">A register.</param>
    /// <param name="value">A value.</param>
    public void WriteRegister16(byte aRegister, ushort value)
    {
        uint xAddr = GetAddressBase(Bus, Slot, Function) | (uint)(aRegister & 0xFC);
        PlatformHAL.PortIO.WriteDWord(ConfigAddressPort, xAddr);
        PlatformHAL.PortIO.WriteWord(ConfigDataPort, value);
    }

    public uint ReadRegister32(byte aRegister)
    {
        uint xAddr = GetAddressBase(Bus, Slot, Function) | (uint)(aRegister & 0xFC);
        PlatformHAL.PortIO.WriteDWord(ConfigAddressPort, xAddr);
        return PlatformHAL.PortIO.ReadDWord(ConfigDataPort);
    }

    public void WriteRegister32(byte aRegister, uint value)
    {
        uint xAddr = GetAddressBase(Bus, Slot, Function) | (uint)(aRegister & 0xFC);
        PlatformHAL.PortIO.WriteDWord(ConfigAddressPort, xAddr);
        PlatformHAL.PortIO.WriteDWord(ConfigDataPort, value);
    }

    #endregion

    /// <summary>
    /// Get address base.
    /// </summary>
    /// <param name="aBus">A bus.</param>
    /// <param name="aSlot">A slot.</param>
    /// <param name="aFunction">A function.</param>
    /// <returns>UInt32 value.</returns>
    protected static uint GetAddressBase(uint aBus, uint aSlot, uint aFunction) =>
        0x80000000 | (aBus << 16) | ((aSlot & 0x1F) << 11) | ((aFunction & 0x07) << 8);

    /// <summary>
    /// Enable memory.
    /// </summary>
    /// <param name="enable">bool value.</param>
    public void EnableMemory(bool enable)
    {
        ushort command = ReadRegister16(0x04);

        ushort flags = 0x0007;

        if (enable)
        {
            command |= flags;
        }
        else
        {
            command &= (ushort)~flags;
        }

        WriteRegister16(0x04, command);
    }

    public void EnableBusMaster(bool enable)
    {
        ushort command = ReadRegister16(0x04);

        ushort flags = 1 << 2;

        if (enable)
        {
            command |= flags;
        }
        else
        {
            command &= (ushort)~flags;
        }

        WriteRegister16(0x04, command);
    }
}
