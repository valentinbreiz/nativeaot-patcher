using System.Runtime.CompilerServices;

namespace Cosmos.Kernel.Core.IO;

/// <summary>
/// Multi-architecture serial port driver.
/// - x86-64: 16550 UART via port I/O (COM1 at 0x3F8)
/// - ARM64: PL011 UART via MMIO (QEMU virt at 0x09000000)
/// </summary>
public static class Serial
{
    #region x86-64 16550 UART Constants

    // COM1 port base address
    internal const ushort COM1_BASE = 0x3F8;

    // Register offsets from base
    internal const ushort REG_DATA = 0;           // Data register (R/W), also divisor latch low when DLAB=1
    internal const ushort REG_IER = 1;            // Interrupt Enable Register, also divisor latch high when DLAB=1
    internal const ushort REG_FCR = 2;            // FIFO Control Register (write only)
    internal const ushort REG_LCR = 3;            // Line Control Register
    internal const ushort REG_MCR = 4;            // Modem Control Register
    internal const ushort REG_LSR = 5;            // Line Status Register (read only)

    // Line Status Register bits
    internal const byte LSR_TX_EMPTY = 0x20;      // Transmit buffer empty
    internal const byte LSR_RX_EMPTY = 1;      // Transmit buffer empty

    // Line Control Register values
    internal const byte LCR_DLAB = 0x80;          // Divisor Latch Access Bit
    internal const byte LCR_8N1 = 0x03;           // 8 data bits, no parity, 1 stop bit

    // FIFO Control Register values
    internal const byte FCR_ENABLE = 0xC7;        // Enable FIFO, clear buffers, 14-byte threshold

    // Modem Control Register values
    internal const byte MCR_DTR_RTS_OUT2 = 0x0B;  // DTR + RTS + OUT2 (enables interrupts)

    // Baud rate divisor for 115200 baud (1.8432 MHz / (16 * 115200) = 1)
    internal const byte BAUD_DIVISOR_LO = 0x01;
    internal const byte BAUD_DIVISOR_HI = 0x00;

    #endregion

    #region ARM64 PL011 UART Constants

    // PL011 UART0 base address (QEMU virt machine)
    internal const ulong PL011_BASE = 0x09000000;

    // Register offsets from base
    internal const ulong PL011_DR = 0x00;         // Data Register
    internal const ulong PL011_FR = 0x18;         // Flag Register
    internal const ulong PL011_IBRD = 0x24;       // Integer Baud Rate Divisor
    internal const ulong PL011_FBRD = 0x28;       // Fractional Baud Rate Divisor
    internal const ulong PL011_LCR_H = 0x2C;      // Line Control Register
    internal const ulong PL011_CR = 0x30;         // Control Register
    internal const ulong PL011_IMSC = 0x38;       // Interrupt Mask Set/Clear

    // Flag Register bits
    internal const uint FR_TXFF = 1 << 5;         // TX FIFO Full
    internal const uint FR_RXFF = 1 << 4;

    // Line Control Register bits
    internal const uint LCR_H_FEN = 1 << 4;       // FIFO Enable
    internal const uint LCR_H_WLEN_8 = 3 << 5;    // 8-bit word length

    // Control Register bits
    internal const uint CR_UARTEN = 1 << 0;       // UART Enable
    internal const uint CR_TXE = 1 << 8;          // Transmit Enable
    internal const uint CR_RXE = 1 << 9;          // Receive Enable

    // Baud rate divisor for 115200 baud (24MHz clock)
    // Divisor = 24000000 / (16 * 115200) = 13.02
    internal const uint PL011_IBRD_115200 = 13;
    internal const uint PL011_FBRD_115200 = 1;

    #endregion

    // String constants for output
    private const string NULL = "null";
    private const string TRUE = "TRUE";
    private const string FALSE = "FALSE";

    /// <summary>
    /// Write a single byte to the serial port.
    /// Waits for transmit buffer to be ready before writing.
    /// </summary>
    public static void ComWrite(byte value)
    {
        if (CosmosFeatures.UARTEnabled)
        {
#if ARCH_ARM64
            // PL011: Wait until TX FIFO is not full
            while ((Native.MMIO.Read32(PL011_BASE + PL011_FR) & FR_TXFF) != 0)
            {
                ;
            }
            Native.MMIO.Write8(PL011_BASE + PL011_DR, value);
#else
            // 16550: Wait for transmit buffer to be empty
            while ((Native.IO.Read8(COM1_BASE + REG_LSR) & LSR_TX_EMPTY) == 0)
            {
                ;
            }

            Native.IO.Write8(COM1_BASE + REG_DATA, value);
#endif
        }
    }

    /// <summary>
    /// Initialize the serial port for 115200 baud, 8N1.
    /// Called from managed Kernel.Initialize()
    /// </summary>
    public static void ComInit()
    {
        if (CosmosFeatures.UARTEnabled)
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

    public static unsafe void WriteString(string str)
    {
        fixed (char* ptr = str)
        {
            for (int i = 0; i < str.Length; i++)
            {
                EarlyGop.PutChar(ptr[i]); // Echo to screen for early debugging
                ComWrite((byte)ptr[i]);
            }
        }
    }

    public static unsafe void WriteNumber(ulong number, bool hex = false)
    {
        if (number == 0)
        {
            EarlyGop.PutChar('0');
            ComWrite((byte)'0');
            return;
        }

        const int maxDigits = 20; // Enough for 64-bit numbers
        byte* buffer = stackalloc byte[maxDigits];
        int index = 0;
        ulong baseValue = hex ? 16u : 10u;

        while (number > 0)
        {
            ulong digit = number % baseValue;
            if (hex && digit >= 10)
            {
                buffer[index] = (byte)('A' + (digit - 10));
            }
            else
            {
                buffer[index] = (byte)('0' + digit);
            }
            number /= baseValue;
            index++;
        }

        // Write digits in reverse order
        for (int i = index - 1; i >= 0; i--)
        {
            EarlyGop.PutChar((char)buffer[i]);
            ComWrite(buffer[i]);
        }
    }

    public static void WriteNumber(uint number, bool hex = false)
    {
        WriteNumber((ulong)number, hex);
    }

    public static void WriteNumber(int number, bool hex = false)
    {
        if (number < 0)
        {
            EarlyGop.PutChar('-');
            ComWrite((byte)'-');
            WriteNumber((ulong)(-number), hex);
        }
        else
        {
            WriteNumber((ulong)number, hex);
        }
    }

    public static void WriteNumber(long number, bool hex = false)
    {
        if (number < 0)
        {
            EarlyGop.PutChar('-');
            ComWrite((byte)'-');
            WriteNumber((ulong)(-number), hex);
        }
        else
        {
            WriteNumber((ulong)number, hex);
        }
    }

    public static void WriteHex(ulong number)
    {
        WriteNumber(number, true);
    }

    public static void WriteHex(uint number)
    {
        WriteNumber((ulong)number, true);
    }

    public static void WriteHexWithPrefix(ulong number)
    {
        WriteString("0x");
        WriteNumber(number, true);
    }

    public static void WriteHexWithPrefix(uint number)
    {
        WriteString("0x");
        WriteNumber((ulong)number, true);
    }

    public static void Write(params object?[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case null:
                    WriteString(NULL);
                    break;
                case string s:
                    WriteString(s);
                    break;
                case char c:
                    WriteString(c.ToString());
                    break;
                case short @short:
                    WriteNumber(@short);
                    break;
                case ushort @ushort:
                    WriteNumber(@ushort);
                    break;
                case int @int:
                    WriteNumber(@int);
                    break;
                case uint @uint:
                    WriteNumber(@uint);
                    break;
                case long @long:
                    WriteNumber(@long);
                    break;
                case ulong @ulong:
                    WriteNumber(@ulong);
                    break;
                case bool @bool:
                    WriteString(@bool ? TRUE : FALSE);
                    break;
                case byte @byte:
                    WriteNumber((ulong)@byte, true);
                    break;
                case byte[] @byteArray:
                    for (int j = 0; j < @byteArray.Length; j++)
                    {
                        WriteNumber((ulong)@byteArray[j], true);
                    }
                    break;
                default:
                    WriteString(args[i].ToString());
                    break;
            }
        }
    }
}
