// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core;
using Cosmos.Kernel.Core.IO;

namespace Cosmos.Kernel.HAL.X64.Cpu;

/// <summary>
/// Legacy 8259 PIC (Programmable Interrupt Controller).
/// Used to disable/mask all IRQs when using APIC.
/// </summary>
public static class LegacyPic
{
    // PIC ports
    private const ushort PIC1_COMMAND = 0x20;
    private const ushort PIC1_DATA = 0x21;
    private const ushort PIC2_COMMAND = 0xA0;
    private const ushort PIC2_DATA = 0xA1;

    /// <summary>
    /// Disables the legacy 8259 PIC by masking all IRQs.
    /// This should be called when using the APIC.
    /// </summary>
    public static void Disable()
    {
        Serial.WriteString("[LegacyPIC] Disabling 8259 PIC...\n");

        // Mask all IRQs on both PICs
        Native.IO.Write8(PIC1_DATA, 0xFF);
        Native.IO.Write8(PIC2_DATA, 0xFF);

        Serial.WriteString("[LegacyPIC] 8259 PIC disabled\n");
    }

    /// <summary>
    /// Remaps and disables the legacy PIC.
    /// Remaps IRQs to vectors 0x20-0x2F to avoid conflicts with CPU exceptions,
    /// then masks all IRQs.
    /// </summary>
    public static void RemapAndDisable()
    {
        Serial.WriteString("[LegacyPIC] Remapping and disabling 8259 PIC...\n");

        // Save masks
        byte mask1 = Native.IO.Read8(PIC1_DATA);
        byte mask2 = Native.IO.Read8(PIC2_DATA);

        // Start initialization sequence (ICW1)
        Native.IO.Write8(PIC1_COMMAND, 0x11);
        Native.IO.Write8(PIC2_COMMAND, 0x11);

        // ICW2: Set vector offsets
        Native.IO.Write8(PIC1_DATA, 0x20);  // PIC1 vectors start at 0x20
        Native.IO.Write8(PIC2_DATA, 0x28);  // PIC2 vectors start at 0x28

        // ICW3: Tell Master PIC there's a slave at IRQ2
        Native.IO.Write8(PIC1_DATA, 0x04);
        Native.IO.Write8(PIC2_DATA, 0x02);

        // ICW4: 8086 mode
        Native.IO.Write8(PIC1_DATA, 0x01);
        Native.IO.Write8(PIC2_DATA, 0x01);

        // Mask all IRQs
        Native.IO.Write8(PIC1_DATA, 0xFF);
        Native.IO.Write8(PIC2_DATA, 0xFF);

        Serial.WriteString("[LegacyPIC] 8259 PIC remapped and disabled\n");
    }
}
