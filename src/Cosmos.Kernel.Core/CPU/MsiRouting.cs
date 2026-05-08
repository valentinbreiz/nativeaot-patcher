// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.Core.CPU;

/// <summary>
/// Architecture-neutral MSI / MSI-X message computation. Both x64 (LAPIC
/// Fixed-mode messages) and ARM64 (GICv3 ITS GITS_TRANSLATER writes)
/// program the same shape into a device's MSI-X table: a 64-bit address
/// plus a 32-bit data dword. The platform initializer registers a backend
/// that produces those values for a given vector and target CPU; HAL-level
/// PCI MSI-X code consumes the result without caring which interrupt
/// controller it ended up on.
/// </summary>
public static class MsiRouting
{
    /// <summary>
    /// Backend signature. Returns the (address, data) the device must
    /// write to deliver <paramref name="vector"/> to <paramref name="targetCpu"/>.
    /// </summary>
    public delegate void ComputeFn(byte vector, uint targetCpu, out ulong address, out uint data);

    private static ComputeFn? s_compute;

    /// <summary>
    /// True once a platform has registered a backend (x64 LAPIC, ARM64 ITS, …).
    /// </summary>
    public static bool IsAvailable => s_compute != null;

    /// <summary>
    /// Called once by the platform interrupt-controller initializer.
    /// </summary>
    public static void RegisterBackend(ComputeFn fn)
    {
        s_compute = fn;
    }

    /// <summary>
    /// Compute the MSI message a device should write to deliver
    /// <paramref name="vector"/> to <paramref name="targetCpu"/>.
    /// Throws if no backend has been registered yet.
    /// </summary>
    public static void ComputeMessage(byte vector, uint targetCpu, out ulong address, out uint data)
    {
        if (s_compute == null)
        {
            throw new System.PlatformNotSupportedException("MSI routing backend not registered");
        }
        s_compute(vector, targetCpu, out address, out data);
    }
}
