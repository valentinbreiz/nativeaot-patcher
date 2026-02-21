// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.Devices;
using Cosmos.Kernel.HAL.Pci.Enums;

namespace Cosmos.Kernel.HAL.Pci;

public class PciDevice : Device
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

    // ARM64 ECAM Base Address (QEMU virt machine VIRT_PCIE_ECAM)
    private const ulong PciEcamBase = 0x3F000000;

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
        Serial.WriteString("[PciDevice] Init");
        Serial.WriteNumber(bus);
        Serial.WriteString(",");
        Serial.WriteNumber(slot);
        Serial.WriteString(",");
        Serial.WriteNumber(function);
        Serial.WriteString("\n");
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

        Serial.WriteString("[PciDevice] Init Done \n");
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
        return ReadConfig8(bus, slot, function, (byte)Config.HeaderType);
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
        return ReadConfig16(bus, slot, function, (byte)Config.VendorId);
    }

    #region IOReadWrite

    public byte ReadRegister8(byte aRegister)
    {
        return ReadConfig8((ushort)Bus, (ushort)Slot, (ushort)Function, aRegister);
    }

    public void WriteRegister8(byte aRegister, byte value)
    {
        WriteConfig8((ushort)Bus, (ushort)Slot, (ushort)Function, aRegister, value);
    }

    public ushort ReadRegister16(byte aRegister)
    {
        return ReadConfig16((ushort)Bus, (ushort)Slot, (ushort)Function, aRegister);
    }

    public void WriteRegister16(byte aRegister, ushort value)
    {
        WriteConfig16((ushort)Bus, (ushort)Slot, (ushort)Function, aRegister, value);
    }

    public uint ReadRegister32(byte aRegister)
    {
        return ReadConfig32((ushort)Bus, (ushort)Slot, (ushort)Function, aRegister);
    }

    public void WriteRegister32(byte aRegister, uint value)
    {
        WriteConfig32((ushort)Bus, (ushort)Slot, (ushort)Function, aRegister, value);
    }

    #endregion

    #region ConfigSpaceAccess

    private static byte ReadConfig8(ushort bus, ushort slot, ushort func, byte offset)
    {
#if ARCH_ARM64
        ulong addr = GetEcamAddress(bus, slot, func, offset);
        return Native.MMIO.Read8(addr);
#else
        uint xAddr = GetAddressBase(bus, slot, func) | (uint)(offset & 0xFC);
        PlatformHAL.PortIO.WriteDWord(ConfigAddressPort, xAddr);
        return (byte)((PlatformHAL.PortIO.ReadDWord(ConfigDataPort) >> (offset % 4 * 8)) & 0xFF);
#endif
    }

    private static void WriteConfig8(ushort bus, ushort slot, ushort func, byte offset, byte value)
    {
#if ARCH_ARM64
        ulong addr = GetEcamAddress(bus, slot, func, offset);
        Native.MMIO.Write8(addr, value);
#else
        uint xAddr = GetAddressBase(bus, slot, func) | (uint)(offset & 0xFC);
        PlatformHAL.PortIO.WriteDWord(ConfigAddressPort, xAddr);
        PlatformHAL.PortIO.WriteByte(ConfigDataPort, value);
#endif
    }

    private static bool _firstAccessLogged = false;

    private static ushort ReadConfig16(ushort bus, ushort slot, ushort func, byte offset)
    {
#if ARCH_ARM64
        ulong addr = GetEcamAddress(bus, slot, func, offset);
        if (!_firstAccessLogged)
        {
            Serial.WriteString("[PciDevice] First ECAM Read: Bus ");
            Serial.WriteNumber(bus);
            Serial.WriteString(" Slot ");
            Serial.WriteNumber(slot);
            Serial.WriteString(" Func ");
            Serial.WriteNumber(func);
            Serial.WriteString(" Offset ");
            Serial.WriteNumber(offset);
            Serial.WriteString(" -> Addr 0x");
            Serial.WriteHex(addr);
            Serial.WriteString("\n");
            _firstAccessLogged = true;
        }
        return Native.MMIO.Read16(addr);
#else
        uint xAddr = GetAddressBase(bus, slot, func) | (uint)(offset & 0xFC);
        PlatformHAL.PortIO.WriteDWord(ConfigAddressPort, xAddr);
        return (ushort)((PlatformHAL.PortIO.ReadDWord(ConfigDataPort) >> (offset % 4 * 8)) & 0xFFFF);
#endif
    }

    private static void WriteConfig16(ushort bus, ushort slot, ushort func, byte offset, ushort value)
    {
#if ARCH_ARM64
        ulong addr = GetEcamAddress(bus, slot, func, offset);
        Native.MMIO.Write16(addr, value);
#else
        uint xAddr = GetAddressBase(bus, slot, func) | (uint)(offset & 0xFC);
        PlatformHAL.PortIO.WriteDWord(ConfigAddressPort, xAddr);
        PlatformHAL.PortIO.WriteWord(ConfigDataPort, value);
#endif
    }

    private static uint ReadConfig32(ushort bus, ushort slot, ushort func, byte offset)
    {
#if ARCH_ARM64
        ulong addr = GetEcamAddress(bus, slot, func, offset);
        return Native.MMIO.Read32(addr);
#else
        uint xAddr = GetAddressBase(bus, slot, func) | (uint)(offset & 0xFC);
        PlatformHAL.PortIO.WriteDWord(ConfigAddressPort, xAddr);
        return PlatformHAL.PortIO.ReadDWord(ConfigDataPort);
#endif
    }

    private static void WriteConfig32(ushort bus, ushort slot, ushort func, byte offset, uint value)
    {
#if ARCH_ARM64
        ulong addr = GetEcamAddress(bus, slot, func, offset);
        Native.MMIO.Write32(addr, value);
#else
        uint xAddr = GetAddressBase(bus, slot, func) | (uint)(offset & 0xFC);
        PlatformHAL.PortIO.WriteDWord(ConfigAddressPort, xAddr);
        PlatformHAL.PortIO.WriteDWord(ConfigDataPort, value);
#endif
    }

    #endregion

    /// <summary>
    /// Get address base for x86 Configuration Mechanism #1.
    /// </summary>
    private static uint GetAddressBase(uint aBus, uint aSlot, uint aFunction) =>
        0x80000000 | (aBus << 16) | ((aSlot & 0x1F) << 11) | ((aFunction & 0x07) << 8);

    /// <summary>
    /// Get ECAM address for ARM64.
    /// </summary>
    private static ulong GetEcamAddress(ushort bus, ushort slot, ushort func, byte offset)
    {
        // ECAM Base + (Bus << 20) + (Device << 15) + (Function << 12) + Offset
        return PciEcamBase + ((ulong)bus << 20) + ((ulong)slot << 15) + ((ulong)func << 12) + offset;
    }

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
