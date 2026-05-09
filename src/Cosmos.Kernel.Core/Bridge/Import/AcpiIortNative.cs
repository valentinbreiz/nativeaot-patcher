// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.InteropServices;

namespace Cosmos.Kernel.Core.Bridge;

/// <summary>
/// Bridge to native ARM64 ACPI IORT resolver
/// (<c>src/Cosmos.Kernel.Native.MultiArch/ACPI/acpi_wrapper.c</c>). Maps a
/// PCI requester ID (segment, BDF) to the ITS DeviceID the platform's
/// IORT advertises. On non-ARM64 builds the native side is a stub that
/// returns failure and the kernel falls back to <c>DeviceID = BDF</c>.
/// </summary>
public static unsafe partial class AcpiIortNative
{
    /// <summary>
    /// Resolves <paramref name="bdf"/> on PCI segment <paramref name="segment"/>
    /// to an ITS DeviceID via IORT. Returns 0 on success.
    /// </summary>
    [LibraryImport("*", EntryPoint = "acpi_iort_resolve_device_id")]
    [SuppressGCTransition]
    public static partial int ResolveDeviceId(uint segment, uint bdf, out uint deviceId);
}
