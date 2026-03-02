// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.InteropServices;
using Cosmos.Kernel.Boot.Limine;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.ARM64.Acpi;

namespace Cosmos.Kernel.HAL.ARM64.Cpu;

/// <summary>
/// Unified GIC facade that auto-detects GICv2 or GICv3 at runtime
/// and delegates to the appropriate implementation.
/// Supports configurable base addresses for real hardware (from DTB).
/// </summary>
public static class GIC
{
    // Interrupt type constants (common to v2 and v3)
    public const uint SGI_START = 0;
    public const uint PPI_START = 16;
    public const uint SPI_START = 32;

    // Timer interrupt IDs (PPIs - same for v2 and v3)
    public const uint TIMER_SECURE_PHYS = 29;
    public const uint TIMER_NONSEC_PHYS = 30;
    public const uint TIMER_VIRT = 27;
    public const uint TIMER_HYP = 26;

    // Default QEMU virt machine addresses (used if DTB not available)
    private const ulong DEFAULT_GICD_BASE = 0x08000000;

    private static bool _isV3;
    private static bool _initialized;
    private static ulong _distBase;

    /// <summary>
    /// Translates a physical address to a virtual address using Limine's HHDM offset.
    /// MMIO and ACPI physical addresses must go through this to be dereferenceable.
    /// If the address is already in the higher half, it's returned as-is.
    /// </summary>
    private static unsafe ulong PhysToVirt(ulong phys)
    {
        if (phys == 0) return 0;

        ulong hhdmOffset = 0;
        if (Limine.HHDM.Response != null)
            hhdmOffset = Limine.HHDM.Response->Offset;

        // Already virtual (above HHDM)?
        if (hhdmOffset != 0 && phys >= hhdmOffset) return phys;

        return phys + hhdmOffset;
    }

    /// <summary>
    /// Whether the GIC has been initialized.
    /// </summary>
    public static bool IsInitialized => _initialized;

    /// <summary>
    /// Whether GICv3 is being used (false = GICv2).
    /// </summary>
    public static bool IsVersion3 => _isV3;

    /// <summary>
    /// Initializes the GIC using addresses from a DTB.
    /// Call this when a device tree is available (real hardware).
    /// </summary>
    /// <param name="dtbAddress">Pointer to the DTB blob in memory.</param>
    public static unsafe void InitializeFromDTB(void* dtbAddress)
    {
        var gicInfo = FDTParser.ParseGIC(dtbAddress);

        if (gicInfo.Found)
        {
            _distBase = gicInfo.DistBase;
            _isV3 = gicInfo.Version >= 3;

            if (_isV3)
            {
                // Real hardware GICv3: use sysreg-only mode (MMIO may be inaccessible)
                Serial.Write("[GIC] DTB: GICv3 GICD=0x");
                Serial.WriteHex(gicInfo.DistBase);
                Serial.Write(" GICR=0x");
                Serial.WriteHex(gicInfo.CpuBase);
                Serial.Write(" (sysreg-only)\n");
                GICv3.Configure(PhysToVirt(gicInfo.DistBase), PhysToVirt(gicInfo.CpuBase));
                GICv3.Initialize(sysregOnly: true);
            }
            else
            {
                // GICv2 always needs MMIO
                DeviceMapper.EnsureMapped(gicInfo.DistBase);
                DeviceMapper.EnsureMapped(gicInfo.CpuBase);
                Serial.Write("[GIC] DTB: GICv2 GICD=0x");
                Serial.WriteHex(gicInfo.DistBase);
                Serial.Write(" GICC=0x");
                Serial.WriteHex(gicInfo.CpuBase);
                Serial.Write("\n");
                GICv2.Configure(PhysToVirt(gicInfo.DistBase), PhysToVirt(gicInfo.CpuBase));
                GICv2.Initialize();
            }
        }
        else
        {
            Serial.Write("[GIC] DTB: GIC not found, falling back to auto-detect\n");
            Initialize();
            return;
        }

        _initialized = true;
    }

    /// <summary>
    /// Initializes the GIC, auto-detecting v2 or v3.
    /// Discovery priority: DTB → ACPI MADT → default QEMU addresses.
    /// </summary>
    public static unsafe void Initialize()
    {
        // Priority 1: Try DTB from Limine bootloader
        if (Limine.DTB.Response != null && Limine.DTB.Response->Address != null)
        {
            Serial.Write("[GIC] Limine DTB available at 0x");
            Serial.WriteHex((ulong)Limine.DTB.Response->Address);
            Serial.Write("\n");
            InitializeFromDTB(Limine.DTB.Response->Address);
            return;
        }

        // Priority 2: Try ACPI MADT (parsed by C code in kmain via acpi_early_init)
        var acpiGic = AcpiGIC.GetGicInfo();
        if (acpiGic != null && acpiGic->Found != 0)
        {
            _distBase = acpiGic->DistBase;
            _isV3 = acpiGic->Version >= 3;

            if (_isV3 && acpiGic->RedistBase != 0)
            {
                // Real hardware GICv3: use sysreg-only mode (MMIO may be inaccessible)
                Serial.Write("[GIC] ACPI: GICv3 GICD=0x");
                Serial.WriteHex(acpiGic->DistBase);
                Serial.Write(" GICR=0x");
                Serial.WriteHex(acpiGic->RedistBase);
                Serial.Write(" (sysreg-only)\n");
                GICv3.Configure(PhysToVirt(acpiGic->DistBase), PhysToVirt(acpiGic->RedistBase));
                GICv3.Initialize(sysregOnly: true);
            }
            else if (_isV3)
            {
                // GICv3 but no GICR in MADT - still use sysreg-only
                Serial.Write("[GIC] ACPI: GICv3 detected (no GICR in MADT, sysreg-only)\n");
                GICv3.Initialize(sysregOnly: true);
            }
            else if (!_isV3 && acpiGic->CpuIfBase != 0)
            {
                // GICv2 always needs MMIO
                DeviceMapper.EnsureMapped(acpiGic->DistBase);
                DeviceMapper.EnsureMapped(acpiGic->CpuIfBase);
                Serial.Write("[GIC] ACPI: GICv2 GICD=0x");
                Serial.WriteHex(acpiGic->DistBase);
                Serial.Write(" GICC=0x");
                Serial.WriteHex(acpiGic->CpuIfBase);
                Serial.Write("\n");
                GICv2.Configure(PhysToVirt(acpiGic->DistBase), PhysToVirt(acpiGic->CpuIfBase));
                GICv2.Initialize();
            }
            else
            {
                // GICv2 but no GICC in MADT - try with MMIO
                DeviceMapper.EnsureMapped(acpiGic->DistBase);
                Serial.Write("[GIC] ACPI: GICv2 detected (no GICC in MADT)\n");
                GICv2.Initialize();
            }

            _initialized = true;
            return;
        }

        // Priority 3: Default QEMU virt machine addresses (no DTB, no ACPI)
        Serial.Write("[GIC] No DTB/ACPI, using default QEMU addresses\n");
        _distBase = PhysToVirt(DEFAULT_GICD_BASE);

        // Map device MMIO into TTBR1 page tables before any MMIO access
        DeviceMapper.EnsureMapped(DEFAULT_GICD_BASE);

        // Detect GIC version via distributor PIDR2.ArchRev
        _isV3 = GICv3.IsGICv3Available(_distBase);

        if (_isV3)
        {
            Serial.Write("[GIC] Detected GICv3\n");
            GICv3.Initialize();
        }
        else
        {
            Serial.Write("[GIC] Detected GICv2\n");
            GICv2.Initialize();
        }

        _initialized = true;
    }

    /// <summary>
    /// Initializes the CPU interface for the current CPU.
    /// </summary>
    public static void InitializeCpuInterface()
    {
        if (_isV3)
            GICv3.InitializeCpuInterface();
        else
            GICv2.InitializeCpuInterface();
    }

    /// <summary>
    /// Enables a specific interrupt.
    /// </summary>
    public static void EnableInterrupt(uint intId)
    {
        if (_isV3)
            GICv3.EnableInterrupt(intId);
        else
            GICv2.EnableInterrupt(intId);
    }

    /// <summary>
    /// Disables a specific interrupt.
    /// </summary>
    public static void DisableInterrupt(uint intId)
    {
        if (_isV3)
            GICv3.DisableInterrupt(intId);
        else
            GICv2.DisableInterrupt(intId);
    }

    /// <summary>
    /// Sets the priority of an interrupt.
    /// </summary>
    public static void SetPriority(uint intId, byte priority)
    {
        if (_isV3)
            GICv3.SetPriority(intId, priority);
        else
            GICv2.SetPriority(intId, priority);
    }

    /// <summary>
    /// Acknowledges an interrupt and returns its ID.
    /// </summary>
    public static uint AcknowledgeInterrupt()
    {
        return _isV3
            ? GICv3.AcknowledgeInterrupt()
            : GICv2.AcknowledgeInterrupt();
    }

    /// <summary>
    /// Signals the end of interrupt processing.
    /// </summary>
    public static void EndOfInterrupt(uint intId)
    {
        if (_isV3)
            GICv3.EndOfInterrupt(intId);
        else
            GICv2.EndOfInterrupt(intId);
    }

    /// <summary>
    /// Checks if an interrupt is pending.
    /// </summary>
    public static bool IsInterruptPending(uint intId)
    {
        return _isV3
            ? GICv3.IsInterruptPending(intId)
            : GICv2.IsInterruptPending(intId);
    }

    /// <summary>
    /// Clears a pending interrupt.
    /// </summary>
    public static void ClearPending(uint intId)
    {
        if (_isV3)
            GICv3.ClearPending(intId);
        else
            GICv2.ClearPending(intId);
    }

    /// <summary>
    /// Configures an interrupt as edge-triggered or level-triggered.
    /// </summary>
    public static void ConfigureInterrupt(uint intId, bool edgeTriggered)
    {
        if (_isV3)
            GICv3.ConfigureInterrupt(intId, edgeTriggered);
        else
            GICv2.ConfigureInterrupt(intId, edgeTriggered);
    }
}
