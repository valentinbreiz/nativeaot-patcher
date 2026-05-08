// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Boot.Limine;
using Cosmos.Kernel.Core;
using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.Core.IO;

namespace Cosmos.Kernel.HAL.Pci;

/// <summary>
/// Programs the MSI-X capability of a PCI device (cap ID 0x11).
/// Architecture-neutral: the message address/data are produced by the
/// platform-registered <see cref="MsiRouting"/> backend, so the same
/// table programming works whether interrupts ultimately land on an x64
/// LAPIC or an ARM64 GICv3 ITS.
/// </summary>
public static class MsiX
{
    public const byte CapId = 0x11;

    private const ushort MsgCtrlEnable = 1 << 15;
    private const ushort MsgCtrlFunctionMask = 1 << 14;
    private const ushort MsgCtrlTableSizeMask = 0x07FF;

    private const ushort PciCommandInterruptDisable = 1 << 10;

    /// <summary>
    /// Per-entry layout (PCI 3.0 §6.8.2.9): 16 bytes, MsgAddrLo @0,
    /// MsgAddrHi @4, MsgData @8, VectorControl @12 (bit 0 = mask).
    /// </summary>
    private const uint EntryStride = 16;
    private const uint EntryAddrLo = 0;
    private const uint EntryAddrHi = 4;
    private const uint EntryData = 8;
    private const uint EntryVectorControl = 12;
    private const uint VectorControlMask = 1;

    /// <summary>
    /// Locates and enables the MSI-X capability on <paramref name="pci"/>:
    /// maps the table BAR via HHDM, masks every entry, sets MSI-X Enable,
    /// clears Function Mask, and disables INTx for the device. Returns
    /// null if the device has no MSI-X capability.
    /// </summary>
    public static unsafe MsiXContext? Enable(PciDevice pci)
    {
        if (!MsiRouting.IsAvailable)
        {
            Serial.WriteString("[MSI-X] no routing backend registered, refusing to enable\n");
            return null;
        }

        byte cap = pci.FindCapability(CapId);
        if (cap == 0)
        {
            return null;
        }

        ushort msgCtrl = pci.ReadRegister16((byte)(cap + 0x02));
        int tableSize = (msgCtrl & MsgCtrlTableSizeMask) + 1;

        uint tableBirOff = pci.ReadRegister32((byte)(cap + 0x04));
        int bir = (int)(tableBirOff & 0x7);
        uint tableOffset = tableBirOff & 0xFFFFFFF8u;

        if (pci.BaseAddressBar == null || bir >= pci.BaseAddressBar.Length)
        {
            Serial.WriteString("[MSI-X] table BIR out of range\n");
            return null;
        }

        PciBaseAddressBar bar = pci.BaseAddressBar[bir];
        if (bar.IsIo)
        {
            Serial.WriteString("[MSI-X] table BAR is I/O, not MMIO\n");
            return null;
        }

        ulong barPhys = bar.BaseAddress;
        if (bar.Is64Bit && bir + 1 < pci.BaseAddressBar.Length)
        {
            ulong upper = pci.ReadRegister32((byte)(0x10 + (bir + 1) * 4));
            barPhys |= upper << 32;
        }

        ulong hhdmOffset = Limine.HHDM.Response != null ? Limine.HHDM.Response->Offset : 0;
        ulong tableVirt = barPhys + hhdmOffset + tableOffset;

        // Mask every entry before turning the function on so no stale
        // garbage in the table can fire as soon as we set MSI-X Enable.
        for (int i = 0; i < tableSize; i++)
        {
            ulong entry = tableVirt + (ulong)i * EntryStride;
            Native.MMIO.Write32(entry + EntryVectorControl, VectorControlMask);
        }

        // Enable MSI-X, clear function mask.
        msgCtrl = (ushort)((msgCtrl & ~MsgCtrlFunctionMask) | MsgCtrlEnable);
        pci.WriteRegister16((byte)(cap + 0x02), msgCtrl);

        // Disable legacy INTx delivery so the same line can't double-fire.
        ushort cmd = pci.ReadRegister16(0x04);
        pci.WriteRegister16(0x04, (ushort)(cmd | PciCommandInterruptDisable));

        Serial.WriteString("[MSI-X] enabled cap=0x");
        Serial.WriteHex(cap);
        Serial.WriteString(" tableSize=");
        Serial.WriteNumber((uint)tableSize);
        Serial.WriteString(" tableVirt=0x");
        Serial.WriteHex(tableVirt);
        Serial.WriteString("\n");

        return new MsiXContext(tableVirt, tableSize);
    }

    /// <summary>
    /// Programs entry <paramref name="index"/> to deliver
    /// <paramref name="vector"/> to <paramref name="targetCpu"/> and
    /// unmasks it.
    /// </summary>
    public static void SetEntry(MsiXContext ctx, int index, byte vector, uint targetCpu = 0)
    {
        if (index < 0 || index >= ctx.EntryCount)
        {
            throw new System.ArgumentOutOfRangeException(nameof(index));
        }

        MsiRouting.ComputeMessage(vector, targetCpu, out ulong address, out uint data);

        ulong entry = ctx.TableVirt + (ulong)index * EntryStride;
        Native.MMIO.Write32(entry + EntryAddrLo, (uint)address);
        Native.MMIO.Write32(entry + EntryAddrHi, (uint)(address >> 32));
        Native.MMIO.Write32(entry + EntryData, data);
        Native.MMIO.Write32(entry + EntryVectorControl, 0);
    }

    public static void MaskEntry(MsiXContext ctx, int index)
    {
        ulong entry = ctx.TableVirt + (ulong)index * EntryStride;
        Native.MMIO.Write32(entry + EntryVectorControl, VectorControlMask);
    }

    public static void UnmaskEntry(MsiXContext ctx, int index)
    {
        ulong entry = ctx.TableVirt + (ulong)index * EntryStride;
        Native.MMIO.Write32(entry + EntryVectorControl, 0);
    }
}

/// <summary>
/// Handle returned by <see cref="MsiX.Enable"/> identifying the mapped
/// MSI-X table for a device.
/// </summary>
public readonly struct MsiXContext
{
    public ulong TableVirt { get; }
    public int EntryCount { get; }

    public MsiXContext(ulong tableVirt, int entryCount)
    {
        TableVirt = tableVirt;
        EntryCount = entryCount;
    }
}
