// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Runtime;

namespace Cosmos.Kernel.HAL.X64.Cpu;

/// <summary>
/// Local APIC (Advanced Programmable Interrupt Controller) driver.
/// Each CPU has its own Local APIC for receiving interrupts.
/// </summary>
public static class LocalApic
{
    // Local APIC register offsets (from base address)
    private const uint LAPIC_ID = 0x20;           // Local APIC ID
    private const uint LAPIC_VERSION = 0x30;      // Local APIC Version
    private const uint LAPIC_TPR = 0x80;          // Task Priority Register
    private const uint LAPIC_EOI = 0xB0;          // End of Interrupt
    private const uint LAPIC_SVR = 0xF0;          // Spurious Interrupt Vector Register
    private const uint LAPIC_ESR = 0x280;         // Error Status Register
    private const uint LAPIC_ICR_LOW = 0x300;     // Interrupt Command Register (low)
    private const uint LAPIC_ICR_HIGH = 0x310;    // Interrupt Command Register (high)
    private const uint LAPIC_TIMER_LVT = 0x320;   // LVT Timer Register
    private const uint LAPIC_LINT0 = 0x350;       // LVT LINT0 Register
    private const uint LAPIC_LINT1 = 0x360;       // LVT LINT1 Register
    private const uint LAPIC_ERROR_LVT = 0x370;   // LVT Error Register

    // SVR bits
    private const uint SVR_ENABLE = 0x100;        // APIC Software Enable
    private const byte SPURIOUS_VECTOR = 0xFF;    // Spurious interrupt vector

    private static ulong _baseAddress;
    private static bool _initialized;

    /// <summary>
    /// Gets the base address of the Local APIC.
    /// </summary>
    public static ulong BaseAddress => _baseAddress;

    /// <summary>
    /// Gets whether the Local APIC is initialized.
    /// </summary>
    public static bool IsInitialized => _initialized;

    /// <summary>
    /// Initializes the Local APIC with the given base address from MADT.
    /// </summary>
    /// <param name="baseAddress">The physical address of the Local APIC registers.</param>
    public static void Initialize(ulong baseAddress)
    {
        _baseAddress = baseAddress;

        Serial.Write("[LocalAPIC] Initializing at 0x", baseAddress.ToString("X"), "\n");

        // Read APIC ID and version for diagnostics
        uint id = Read(LAPIC_ID);
        uint version = Read(LAPIC_VERSION);

        Serial.Write("[LocalAPIC] ID: ", (id >> 24), "\n");
        Serial.Write("[LocalAPIC] Version: 0x", version.ToString("X"), "\n");

        // Clear error status register (requires two writes)
        Write(LAPIC_ESR, 0);
        Write(LAPIC_ESR, 0);

        // Set Task Priority to 0 to accept all interrupts
        Write(LAPIC_TPR, 0);

        // Configure spurious interrupt vector and enable APIC
        uint svr = SPURIOUS_VECTOR | SVR_ENABLE;
        Write(LAPIC_SVR, svr);

        Serial.Write("[LocalAPIC] SVR set to 0x", svr.ToString("X"), "\n");

        // Mask LINT0 and LINT1 (we use I/O APIC for external interrupts)
        Write(LAPIC_LINT0, 0x10000);  // Masked
        Write(LAPIC_LINT1, 0x10000);  // Masked

        // Mask timer and error LVT entries
        Write(LAPIC_TIMER_LVT, 0x10000);  // Masked
        Write(LAPIC_ERROR_LVT, 0x10000);  // Masked

        _initialized = true;
        Serial.Write("[LocalAPIC] Initialization complete\n");
    }

    /// <summary>
    /// Sends End of Interrupt signal to the Local APIC.
    /// Must be called at the end of every interrupt handler.
    /// </summary>
    public static void SendEOI()
    {
        if (_initialized)
        {
            Write(LAPIC_EOI, 0);
        }
    }

    /// <summary>
    /// Reads the In-Service Register to check which interrupts are being serviced.
    /// Returns the ISR value for vectors 32-63 (ISR1).
    /// </summary>
    public static uint GetISR1()
    {
        // ISR is at offset 0x100-0x170 (8 32-bit registers covering vectors 0-255)
        // ISR1 at 0x110 covers vectors 32-63
        return Read(0x110);
    }

    /// <summary>
    /// Reads the Interrupt Request Register to check pending interrupts.
    /// Returns the IRR value for vectors 32-63 (IRR1).
    /// </summary>
    public static uint GetIRR1()
    {
        // IRR is at offset 0x200-0x270 (8 32-bit registers covering vectors 0-255)
        // IRR1 at 0x210 covers vectors 32-63
        return Read(0x210);
    }

    /// <summary>
    /// Gets the current Local APIC ID.
    /// </summary>
    public static byte GetId()
    {
        return (byte)(Read(LAPIC_ID) >> 24);
    }

    /// <summary>
    /// Reads a 32-bit value from a Local APIC register.
    /// </summary>
    private static uint Read(uint offset)
    {
        return Native.MMIO.Read32(_baseAddress + offset);
    }

    /// <summary>
    /// Writes a 32-bit value to a Local APIC register.
    /// </summary>
    private static void Write(uint offset, uint value)
    {
        Native.MMIO.Write32(_baseAddress + offset, value);
    }
}
