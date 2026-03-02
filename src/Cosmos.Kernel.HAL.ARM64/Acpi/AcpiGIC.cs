// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Cosmos.Kernel.HAL.ARM64.Acpi;

/// <summary>
/// C# bridge to native ACPI GIC discovery (acpi_wrapper.c in MultiArch).
/// The native code is called during early boot (kmain) and parses the MADT
/// to extract GICD/GICR/GICC addresses. This class just retrieves the result.
/// </summary>
public static unsafe partial class AcpiGIC
{
    /// <summary>
    /// Mirrors the C struct acpi_gic_info_t from ACPI/acpi_wrapper.c.
    /// Must match the native layout exactly.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GicInfo
    {
        public byte Found;
        public byte Version;       // GIC version: 2 or 3
        private fixed byte _pad[6];
        public ulong DistBase;     // GICD physical base
        public ulong RedistBase;   // GICR physical base (GICv3)
        public ulong RedistLength; // GICR region length
        public ulong CpuIfBase;    // GICC physical base (GICv2)
    }

    [LibraryImport("*", EntryPoint = "acpi_get_gic_info")]
    [SuppressGCTransition]
    private static partial GicInfo* NativeGetGicInfo();

    /// <summary>
    /// Gets GIC information discovered from ACPI MADT during early boot.
    /// Returns null if ACPI was not available or GIC entries weren't found.
    /// </summary>
    public static GicInfo* GetGicInfo()
    {
        return NativeGetGicInfo();
    }
}
