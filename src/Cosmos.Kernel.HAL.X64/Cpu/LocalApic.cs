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
    private const uint LAPIC_THERMAL_LVT = 0x330; // LVT Thermal Sensor Register
    private const uint LAPIC_PERF_LVT = 0x340;    // LVT Performance Counter Register
    private const uint LAPIC_LINT0 = 0x350;       // LVT LINT0 Register
    private const uint LAPIC_LINT1 = 0x360;       // LVT LINT1 Register
    private const uint LAPIC_ERROR_LVT = 0x370;   // LVT Error Register
    private const uint LAPIC_TIMER_INIT = 0x380;  // Timer Initial Count Register
    private const uint LAPIC_TIMER_CURRENT = 0x390; // Timer Current Count Register
    private const uint LAPIC_TIMER_DIVIDE = 0x3E0;  // Timer Divide Configuration Register

    // Timer LVT bits
    private const uint TIMER_MASKED = 0x10000;    // Timer interrupt masked
    private const uint TIMER_PERIODIC = 0x20000;  // Periodic mode (vs one-shot)
    private const byte TIMER_VECTOR = 0x20;       // Timer interrupt vector (32)

    // Timer divide values (divide by 1, 2, 4, 8, 16, 32, 64, 128)
    private const uint TIMER_DIVIDE_BY_1 = 0xB;
    private const uint TIMER_DIVIDE_BY_16 = 0x3;

    // PIT ports for calibration
    private const ushort PIT_CHANNEL0_DATA = 0x40;
    private const ushort PIT_COMMAND = 0x43;
    private const uint PIT_FREQUENCY = 1193182;   // PIT base frequency in Hz

    // SVR bits
    private const uint SVR_ENABLE = 0x100;        // APIC Software Enable
    private const byte SPURIOUS_VECTOR = 0xFF;    // Spurious interrupt vector

    private static ulong _baseAddress;
    private static bool _initialized;
    private static uint _ticksPerMs;
    private static bool _timerCalibrated;

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

    /// <summary>
    /// Gets whether the LAPIC timer has been calibrated.
    /// </summary>
    public static bool IsTimerCalibrated => _timerCalibrated;

    /// <summary>
    /// Gets the calibrated ticks per millisecond.
    /// </summary>
    public static uint TicksPerMs => _ticksPerMs;

    /// <summary>
    /// Calibrates the LAPIC timer using the PIT as a reference.
    /// Must be called after Initialize() and before using timer functions.
    /// </summary>
    public static void CalibrateTimer()
    {
        if (!_initialized)
        {
            Serial.Write("[LocalAPIC] ERROR: Cannot calibrate timer - APIC not initialized\n");
            return;
        }

        Serial.Write("[LocalAPIC] Calibrating timer using PIT...\n");

        // Set timer divide to 16
        Write(LAPIC_TIMER_DIVIDE, TIMER_DIVIDE_BY_16);

        // Configure PIT channel 0 for one-shot mode, ~10ms delay
        // 10ms = 11932 ticks at 1193182 Hz
        const ushort pitCount = 11932;

        // PIT command: channel 0, lobyte/hibyte, one-shot mode, binary
        Native.IO.Write8(PIT_COMMAND, 0x30);
        Native.IO.Write8(PIT_CHANNEL0_DATA, (byte)(pitCount & 0xFF));
        Native.IO.Write8(PIT_CHANNEL0_DATA, (byte)(pitCount >> 8));

        // Set LAPIC timer to max initial count (one-shot, masked)
        Write(LAPIC_TIMER_LVT, TIMER_MASKED);
        Write(LAPIC_TIMER_INIT, 0xFFFFFFFF);

        // Wait for PIT to count down by polling
        // Read back current count until it wraps or reaches near zero
        ushort lastCount = pitCount;
        while (true)
        {
            // Latch count for channel 0
            Native.IO.Write8(PIT_COMMAND, 0x00);
            byte lo = Native.IO.Read8(PIT_CHANNEL0_DATA);
            byte hi = Native.IO.Read8(PIT_CHANNEL0_DATA);
            ushort currentCount = (ushort)(lo | (hi << 8));

            // PIT counts down, check if we've passed our target
            if (currentCount > lastCount || currentCount == 0)
                break;
            lastCount = currentCount;
        }

        // Read how many LAPIC ticks elapsed
        uint lapicTicksElapsed = 0xFFFFFFFF - Read(LAPIC_TIMER_CURRENT);

        // Stop the timer
        Write(LAPIC_TIMER_INIT, 0);

        // Calculate ticks per ms (we waited ~10ms)
        // Account for divide by 16
        _ticksPerMs = lapicTicksElapsed / 10;

        _timerCalibrated = true;
        Serial.Write("[LocalAPIC] Timer calibrated: ", _ticksPerMs, " ticks/ms\n");
    }

    /// <summary>
    /// Blocks for the specified number of milliseconds using the LAPIC timer.
    /// </summary>
    /// <param name="ms">Number of milliseconds to wait.</param>
    public static void Wait(uint ms)
    {
        if (!_timerCalibrated)
        {
            Serial.Write("[LocalAPIC] ERROR: Timer not calibrated\n");
            return;
        }

        if (ms == 0)
            return;

        // Set timer divide to 16 (same as calibration)
        Write(LAPIC_TIMER_DIVIDE, TIMER_DIVIDE_BY_16);

        // Calculate ticks needed
        uint ticks = _ticksPerMs * ms;

        // Set up one-shot timer (masked - we poll instead of interrupt)
        Write(LAPIC_TIMER_LVT, TIMER_MASKED);
        Write(LAPIC_TIMER_INIT, ticks);

        // Poll until timer reaches zero
        while (Read(LAPIC_TIMER_CURRENT) > 0)
        {
            // Busy wait
        }
    }

    /// <summary>
    /// Starts the LAPIC timer in periodic mode with the given interval.
    /// Will fire interrupt on TIMER_VECTOR (0x20 = 32).
    /// </summary>
    /// <param name="intervalMs">Interval in milliseconds between interrupts.</param>
    public static void StartPeriodicTimer(uint intervalMs)
    {
        if (!_timerCalibrated)
        {
            Serial.Write("[LocalAPIC] ERROR: Timer not calibrated\n");
            return;
        }

        uint ticks = _ticksPerMs * intervalMs;

        Serial.Write("[LocalAPIC] Starting periodic timer: ", intervalMs, "ms (", ticks, " ticks)\n");

        // Set timer divide to 16
        Write(LAPIC_TIMER_DIVIDE, TIMER_DIVIDE_BY_16);

        // Configure timer: periodic mode, unmasked, vector 0x20
        Write(LAPIC_TIMER_LVT, TIMER_PERIODIC | TIMER_VECTOR);

        // Set initial count to start the timer
        Write(LAPIC_TIMER_INIT, ticks);
    }

    /// <summary>
    /// Stops the LAPIC timer.
    /// </summary>
    public static void StopTimer()
    {
        // Mask the timer and set count to 0
        Write(LAPIC_TIMER_LVT, TIMER_MASKED);
        Write(LAPIC_TIMER_INIT, 0);
    }
}
