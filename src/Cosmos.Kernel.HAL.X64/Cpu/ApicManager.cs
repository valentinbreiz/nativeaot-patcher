// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.Acpi;

namespace Cosmos.Kernel.HAL.X64.Cpu;

/// <summary>
/// Manages APIC initialization and configuration.
/// </summary>
public static class ApicManager
{
    private static bool _initialized;

    /// <summary>
    /// Gets whether the APIC system is initialized.
    /// </summary>
    public static bool IsInitialized => _initialized;

    /// <summary>
    /// Initializes the APIC system using MADT information.
    /// </summary>
    public static unsafe void Initialize()
    {
        Serial.Write("[ApicManager] Starting APIC initialization...\n");

        // Disable the legacy 8259 PIC first
        LegacyPic.RemapAndDisable();

        // Get MADT info
        MadtInfo* madtPtr = Acpi.Acpi.GetMadtInfoPtr();
        if (madtPtr == null)
        {
            Serial.Write("[ApicManager] ERROR: MADT not available!\n");
            return;
        }

        MadtInfo madt = *madtPtr;

        // Initialize Local APIC
        Serial.Write("[ApicManager] Initializing Local APIC...\n");
        LocalApic.Initialize(madt.LocalApicAddress);

        // Calibrate LAPIC timer
        Serial.Write("[ApicManager] Calibrating LAPIC timer...\n");
        LocalApic.CalibrateTimer();

        // Initialize I/O APIC(s)
        var ioApics = madt.IoApics;
        if (ioApics.Length > 0)
        {
            Serial.Write("[ApicManager] Initializing I/O APIC...\n");
            // For now, just use the first I/O APIC
            IoApic.Initialize(ioApics[0]);
        }
        else
        {
            Serial.Write("[ApicManager] WARNING: No I/O APIC found in MADT!\n");
        }

        _initialized = true;
        Serial.Write("[ApicManager] APIC system initialized\n");
    }

    /// <summary>
    /// Routes an ISA IRQ to an interrupt vector.
    /// Handles IRQ overrides from MADT automatically.
    /// </summary>
    /// <param name="irq">ISA IRQ number (0-15).</param>
    /// <param name="vector">Target interrupt vector.</param>
    /// <param name="startMasked">If true, the IRQ starts masked and must be explicitly unmasked.</param>
    public static unsafe void RouteIrq(byte irq, byte vector, bool startMasked = false)
    {
        if (!_initialized)
        {
            Serial.Write("[ApicManager] ERROR: APIC not initialized!\n");
            return;
        }

        MadtInfo* madtPtr = Acpi.Acpi.GetMadtInfoPtr();
        if (madtPtr == null) return;

        MadtInfo madt = *madtPtr;

        // Check for IRQ override
        IrqOverride? irqOverride = null;
        foreach (var iso in madt.Overrides)
        {
            if (iso.Source == irq)
            {
                irqOverride = iso;
                break;
            }
        }

        // Route through I/O APIC
        byte targetApicId = LocalApic.GetId();
        IoApic.RouteIrq(irq, vector, targetApicId, irqOverride, startMasked);
    }

    /// <summary>
    /// Sends End of Interrupt signal.
    /// Must be called at the end of every interrupt handler.
    /// </summary>
    public static void SendEOI()
    {
        LocalApic.SendEOI();
    }

    /// <summary>
    /// Masks (disables) an IRQ at the I/O APIC level.
    /// </summary>
    public static void MaskIrq(byte irq)
    {
        IoApic.MaskIrq(irq);
    }

    /// <summary>
    /// Unmasks (enables) an IRQ at the I/O APIC level.
    /// </summary>
    public static void UnmaskIrq(byte irq)
    {
        IoApic.UnmaskIrq(irq);
    }

    /// <summary>
    /// Blocks for the specified number of milliseconds using the LAPIC timer.
    /// </summary>
    /// <param name="ms">Number of milliseconds to wait.</param>
    public static void Wait(uint ms)
    {
        LocalApic.Wait(ms);
    }

    /// <summary>
    /// Gets whether the LAPIC timer is calibrated.
    /// </summary>
    public static bool IsTimerCalibrated => LocalApic.IsTimerCalibrated;
}
