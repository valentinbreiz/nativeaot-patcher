using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Cosmos.Kernel.Core.Async;

namespace Cosmos.Kernel.Core.IO;

/// <summary>
/// Multi-architecture serial port driver.
/// - x86-64: 16550 UART via port I/O (COM1 at 0x3F8)
/// - ARM64: PL011 UART via MMIO (QEMU virt at 0x09000000)
/// </summary>
public static partial class Serial
{
    #region x86-64 16550 UART Constants

    // COM1 port base address
    private const ushort COM1_BASE = 0x3F8;
    private const ushort COM2_BASE = 0x2F8;
    private const ushort COM3_BASE = 0x3E8;
    private const ushort COM4_BASE = 0x2E8;
    private const ushort COM5_BASE = 0x5F8;
    private const ushort COM6_BASE = 0x4F8;
    private const ushort COM7_BASE = 0x5E8;
    private const ushort COM8_BASE = 0x4E8;

    // Register offsets from base
    private const ushort REG_DATA = 0;           // Data register (R/W), also divisor latch low when DLAB=1
    private const ushort REG_IER = 1;            // Interrupt Enable Register, also divisor latch high when DLAB=1
    private const ushort REG_FCR = 2;            // FIFO Control Register (write only)
    private const ushort REG_LCR = 3;            // Line Control Register
    private const ushort REG_MCR = 4;            // Modem Control Register
    private const ushort REG_LSR = 5;            // Line Status Register (read only)

    // Line Status Register bits
    private const byte LSR_TX_EMPTY = 0x20;      // Transmit buffer empty

    // Line Control Register values
    private const byte LCR_DLAB = 0x80;          // Divisor Latch Access Bit
    private const byte LCR_8N1 = 0x03;           // 8 data bits, no parity, 1 stop bit

    // FIFO Control Register values
    private const byte FCR_ENABLE = 0xC7;        // Enable FIFO, clear buffers, 14-byte threshold

    // Modem Control Register values
    private const byte MCR_DTR_RTS_OUT2 = 0x0B;  // DTR + RTS + OUT2 (enables interrupts)

    // Baud rate divisor for 115200 baud (1.8432 MHz / (16 * 115200) = 1)
    private const byte BAUD_DIVISOR_LO = 0x01;
    private const byte BAUD_DIVISOR_HI = 0x00;

    #endregion

    #region ARM64 PL011 UART Constants

    // PL011 UART0 base address (QEMU virt machine)
    private const ulong PL011_BASE = 0x09000000;

    // Register offsets from base
    private const ulong PL011_DR = 0x00;         // Data Register
    private const ulong PL011_FR = 0x18;         // Flag Register
    private const ulong PL011_IBRD = 0x24;       // Integer Baud Rate Divisor
    private const ulong PL011_FBRD = 0x28;       // Fractional Baud Rate Divisor
    private const ulong PL011_LCR_H = 0x2C;      // Line Control Register
    private const ulong PL011_CR = 0x30;         // Control Register
    private const ulong PL011_IMSC = 0x38;       // Interrupt Mask Set/Clear

    // Flag Register bits
    private const uint FR_TXFF = 1 << 5;         // TX FIFO Full

    // Line Control Register bits
    private const uint LCR_H_FEN = 1 << 4;       // FIFO Enable
    private const uint LCR_H_WLEN_8 = 3 << 5;    // 8-bit word length

    // Control Register bits
    private const uint CR_UARTEN = 1 << 0;       // UART Enable
    private const uint CR_TXE = 1 << 8;          // Transmit Enable
    private const uint CR_RXE = 1 << 9;          // Receive Enable

    // Baud rate divisor for 115200 baud (24MHz clock)
    // Divisor = 24000000 / (16 * 115200) = 13.02
    private const uint PL011_IBRD_115200 = 13;
    private const uint PL011_FBRD_115200 = 1;

    #endregion

    // String constants for output
    private const string NULL = "null";
    private const string TRUE = "TRUE";
    private const string FALSE = "FALSE";

    /// <summary>
    /// Initialize the serial port for 115200 baud, 8N1.
    /// Called from managed Kernel.Initialize()
    /// </summary>
    public static void ComInit()
    {
#if ARCH_ARM64
        // === PL011 UART Initialization ===

        // Disable UART before configuration
        Native.MMIO.Write32(PL011_BASE + PL011_CR, 0);

        // Clear all interrupt masks
        Native.MMIO.Write32(PL011_BASE + PL011_IMSC, 0);

        // Set baud rate to 115200 (24MHz clock)
        Native.MMIO.Write32(PL011_BASE + PL011_IBRD, PL011_IBRD_115200);
        Native.MMIO.Write32(PL011_BASE + PL011_FBRD, PL011_FBRD_115200);

        // Configure: 8 data bits, FIFO enabled
        Native.MMIO.Write32(PL011_BASE + PL011_LCR_H, LCR_H_FEN | LCR_H_WLEN_8);

        // Enable UART, TX, and RX
        Native.MMIO.Write32(PL011_BASE + PL011_CR, CR_UARTEN | CR_TXE | CR_RXE);
#else
        // === 16550 UART Initialization ===

        // Disable all interrupts
        Native.IO.Write8(COM1_BASE + REG_IER, 0x00);

        // Enable DLAB to set baud rate divisor
        Native.IO.Write8(COM1_BASE + REG_LCR, LCR_DLAB);

        // Set baud rate divisor for 115200 baud
        Native.IO.Write8(COM1_BASE + REG_DATA, BAUD_DIVISOR_LO);  // Divisor low byte
        Native.IO.Write8(COM1_BASE + REG_IER, BAUD_DIVISOR_HI);   // Divisor high byte

        // Configure: 8 data bits, no parity, 1 stop bit (clears DLAB)
        Native.IO.Write8(COM1_BASE + REG_LCR, LCR_8N1);

        // Enable and clear FIFOs, set 14-byte threshold
        Native.IO.Write8(COM1_BASE + REG_FCR, FCR_ENABLE);

        // Enable DTR, RTS, and OUT2 (required for interrupts)
        Native.IO.Write8(COM1_BASE + REG_MCR, MCR_DTR_RTS_OUT2);
#endif
    }

}
