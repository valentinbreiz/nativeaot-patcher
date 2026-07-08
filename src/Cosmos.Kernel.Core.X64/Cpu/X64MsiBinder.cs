// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core.CPU;

namespace Cosmos.Kernel.Core.X64.Cpu;

/// <summary>
/// x64 LAPIC implementation of <see cref="IMsiBinder"/>. Allocates an IDT
/// vector for each MSI-X entry; the device's MSI-X table writes
/// <c>data = vector</c> to the LAPIC's MSI doorbell at
/// <c>0xFEE00000 | (apicId &lt;&lt; 12)</c>.
/// </summary>
internal sealed class X64MsiBinder : IMsiBinder
{
    /// <summary>MSI message address base for x64 (Intel SDM Vol. 3, "Message Address Register Format").</summary>
    private const ulong MsiAddressBase = 0xFEE00000UL;

    /// <summary>Bit position of the Destination ID field in the MSI message address (bits 19:12).</summary>
    private const int MsiDestinationIdShift = 12;

    public bool IsAvailable => LocalApic.IsInitialized;

    /// <summary>
    /// x64 MSI delivery is per-vector, not per-device — the LAPIC has no
    /// device-side state to allocate. Returns null.
    /// </summary>
    public object? PrepareDevice(uint bus, uint slot, uint function, int entryCount) => null;

    public void BindEntry(object? deviceCtx, int entryIndex, InterruptManager.IrqDelegate handler,
                          uint targetCpu, out ulong address, out uint data)
    {
        byte vector = InterruptManager.AllocateVector(handler);
        // The CPU index is not the LAPIC destination ID: the BSP's APIC ID
        // is not architecturally guaranteed to be 0 (QEMU's is; real
        // multi-socket parts often differ), and an MSI aimed at a
        // nonexistent LAPIC is silently dropped. Until SMP brings a
        // MADT-backed index→APIC-ID map, resolve the boot CPU's real ID at
        // bind time; other indices are unreachable today (single-CPU).
        byte apicId = targetCpu == 0 ? LocalApic.GetId() : (byte)targetCpu;
        address = MsiAddressBase | ((ulong)apicId << MsiDestinationIdShift);
        data = vector;
    }
}
