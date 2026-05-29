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
        byte apicId = (byte)targetCpu;
        address = 0xFEE00000UL | ((ulong)apicId << 12);
        data = vector;
    }
}
