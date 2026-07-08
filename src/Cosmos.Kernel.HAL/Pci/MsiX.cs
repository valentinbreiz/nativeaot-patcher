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

    /// <summary>Offset of the Message Control register within the MSI-X capability (PCI 3.0 §6.8.2.3).</summary>
    private const byte MsgCtrlOffset = 0x02;
    /// <summary>Offset of the Table Offset/Table BIR register within the MSI-X capability (PCI 3.0 §6.8.2.4).</summary>
    private const byte TableOffsetBirOffset = 0x04;
    /// <summary>Offset of the Command register in the PCI configuration header.</summary>
    private const byte PciCommandOffset = 0x04;

    /// <summary>Mask selecting the BAR Indicator Register (BIR) bits of the Table Offset/BIR register.</summary>
    private const uint TableBirMask = 0x7;
    /// <summary>Mask selecting the QWORD-aligned table offset bits of the Table Offset/BIR register.</summary>
    private const uint TableOffsetMask = 0xFFFFFFF8u;

    /// <summary>Right shift extracting the high 32 bits of the 64-bit message address.</summary>
    private const int AddrHighDwordShift = 32;

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

        ushort msgCtrl = pci.ReadRegister16((byte)(cap + MsgCtrlOffset));
        int tableSize = (msgCtrl & MsgCtrlTableSizeMask) + 1;

        uint tableBirOff = pci.ReadRegister32((byte)(cap + TableOffsetBirOffset));
        int bir = (int)(tableBirOff & TableBirMask);
        uint tableOffset = tableBirOff & TableOffsetMask;

        ulong barPhys = pci.GetBar64Address(bir);
        if (barPhys == 0)
        {
            Serial.WriteString("[MSI-X] table BAR is I/O or out of range\n");
            return null;
        }

        ulong hhdmOffset = Limine.HHDM.Response != null ? Limine.HHDM.Response->Offset : 0;
        ulong tableVirt = barPhys + hhdmOffset + tableOffset;

        // The table BAR is not necessarily the register BAR the driver
        // already mapped (QEMU's NVMe puts the table in its own BAR): make
        // sure both ends of the table's HHDM alias are device-mapped before
        // the masking loop below dereferences it. No-op on x64.
        PlatformHAL.Initializer?.EnsureMmioMapped(barPhys + tableOffset);
        PlatformHAL.Initializer?.EnsureMmioMapped(barPhys + tableOffset + (ulong)tableSize * EntryStride - 1);

        // Mask every entry before turning the function on so no stale
        // garbage in the table can fire as soon as we set MSI-X Enable.
        for (int i = 0; i < tableSize; i++)
        {
            ulong entry = tableVirt + (ulong)i * EntryStride;
            Native.MMIO.Write32(entry + EntryVectorControl, VectorControlMask);
        }

        // Per-arch device prep (ARM64 ITS allocates an ITT + MAPDs the
        // device here; x64 returns null). A binder that cannot route this
        // device (e.g. its ITS DeviceID exceeds the device table) throws —
        // turn that into "no MSI-X" so the driver takes its polled
        // fallback instead of enabling MSI-X that can never deliver.
        object? deviceCtx;
        try
        {
            deviceCtx = MsiRouting.PrepareDevice(pci.Bus, pci.Slot, pci.Function, tableSize);
        }
        catch (System.InvalidOperationException)
        {
            Serial.WriteString("[MSI-X] platform binder rejected the device, leaving MSI-X disabled\n");
            return null;
        }

        // Enable MSI-X, clear function mask.
        msgCtrl = (ushort)((msgCtrl & ~MsgCtrlFunctionMask) | MsgCtrlEnable);
        pci.WriteRegister16((byte)(cap + MsgCtrlOffset), msgCtrl);

        // Disable legacy INTx delivery so the same line can't double-fire.
        ushort cmd = pci.ReadRegister16(PciCommandOffset);
        pci.WriteRegister16(PciCommandOffset, (ushort)(cmd | PciCommandInterruptDisable));

        return new MsiXContext(tableVirt, tableSize, deviceCtx);
    }

    /// <summary>
    /// Allocate a routing slot (IDT vector on x64, LPI on ARM64) for
    /// <paramref name="handler"/> via <see cref="MsiRouting"/>, then
    /// program entry <paramref name="index"/> to deliver to it and unmask.
    /// </summary>
    public static void SetEntry(MsiXContext ctx, int index, InterruptManager.IrqDelegate handler, uint targetCpu = 0)
    {
        if (index < 0 || index >= ctx.EntryCount)
        {
            throw new System.ArgumentOutOfRangeException(nameof(index));
        }

        MsiRouting.BindEntry(ctx.DeviceCtx, index, handler, targetCpu, out ulong address, out uint data);

        ulong entry = ctx.TableVirt + (ulong)index * EntryStride;
        Native.MMIO.Write32(entry + EntryAddrLo, (uint)address);
        Native.MMIO.Write32(entry + EntryAddrHi, (uint)(address >> AddrHighDwordShift));
        Native.MMIO.Write32(entry + EntryData, data);
        Native.MMIO.Write32(entry + EntryVectorControl, 0);
    }

    public static void MaskEntry(MsiXContext ctx, int index)
    {
        if (index < 0 || index >= ctx.EntryCount)
        {
            throw new System.ArgumentOutOfRangeException(nameof(index));
        }

        ulong entry = ctx.TableVirt + (ulong)index * EntryStride;
        Native.MMIO.Write32(entry + EntryVectorControl, VectorControlMask);
    }

    public static void UnmaskEntry(MsiXContext ctx, int index)
    {
        if (index < 0 || index >= ctx.EntryCount)
        {
            throw new System.ArgumentOutOfRangeException(nameof(index));
        }

        ulong entry = ctx.TableVirt + (ulong)index * EntryStride;
        Native.MMIO.Write32(entry + EntryVectorControl, 0);
    }
}

/// <summary>
/// Handle returned by <see cref="MsiX.Enable"/> identifying the mapped
/// MSI-X table for a device. Carries the platform-binder's per-device
/// state (e.g. ARM64 ITS DeviceID + ITT pointer) so subsequent
/// <see cref="MsiX.SetEntry"/> calls can route through the same context.
/// </summary>
public readonly struct MsiXContext
{
    public ulong TableVirt { get; }
    public int EntryCount { get; }
    public object? DeviceCtx { get; }

    public MsiXContext(ulong tableVirt, int entryCount, object? deviceCtx)
    {
        TableVirt = tableVirt;
        EntryCount = entryCount;
        DeviceCtx = deviceCtx;
    }
}
