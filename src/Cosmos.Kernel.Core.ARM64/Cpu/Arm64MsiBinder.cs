// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core.Bridge;
using Cosmos.Kernel.Core.CPU;

namespace Cosmos.Kernel.Core.ARM64.Cpu;

/// <summary>
/// ARM64 GICv3 ITS implementation of <see cref="IMsiBinder"/>. For each
/// device <see cref="PrepareDevice"/> resolves the PCI BDF to an ITS
/// DeviceID through ACPI IORT (with identity fallback when IORT is
/// missing) and issues <c>MAPD</c>; for each MSI-X entry
/// <see cref="BindEntry"/> allocates an LPI, enables it, wires up
/// <c>(DeviceID, EventID = entryIndex)</c> via <c>MAPTI</c>, and returns
/// <c>(GITS_TRANSLATER_phys, EventID)</c> as the (addr, data) the device
/// will write.
/// </summary>
internal sealed class Arm64MsiBinder : IMsiBinder
{
    private sealed class Arm64DevCtx
    {
        public uint DeviceId;
        public int EntryCount;
    }

    public bool Available => GICv3Its.IsInitialized && GICv3Lpi.IsInitialized;

    public object? PrepareDevice(uint bus, uint slot, uint function, int entryCount)
    {
        uint bdf = (bus << 8) | (slot << 3) | function;
        // Resolve PCI requester ID -> ITS DeviceID via IORT. Fall back to
        // identity (DeviceID == BDF) if no IORT is present.
        if (AcpiIortNative.ResolveDeviceId(0, bdf, out uint devId) != 0)
        {
            devId = bdf;
        }

        GICv3Its.MapDevice(devId, (uint)entryCount);
        return new Arm64DevCtx { DeviceId = devId, EntryCount = entryCount };
    }

    public void BindEntry(object? deviceCtx, int entryIndex, InterruptManager.IrqDelegate handler,
                          uint targetCpu, out ulong address, out uint data)
    {
        if (deviceCtx is not Arm64DevCtx ctx)
        {
            throw new System.InvalidOperationException("Arm64MsiBinder: PrepareDevice was not called");
        }

        uint lpi = InterruptManager.AllocateLpi(handler);
        GICv3Lpi.EnableLpi(lpi);
        GICv3Its.MapEvent(ctx.DeviceId, (uint)entryIndex, lpi);

        address = GICv3Its.TranslaterPhysAddr;
        data = (uint)entryIndex;
    }
}
