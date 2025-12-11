using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.Runtime;

namespace Cosmos.Kernel.Core.IO;

public static class Serial
{
    // x86-64 COM1 Serial Port Constants
    private const ushort COM1_BASE = 0x3F8;
    private const ushort COM1_DATA = COM1_BASE + 0;           // Data register (R/W)
    private const ushort COM1_IER = COM1_BASE + 1;            // Interrupt Enable Register
    private const ushort COM1_FCR = COM1_BASE + 2;            // FIFO Control Register
    private const ushort COM1_LCR = COM1_BASE + 3;            // Line Control Register
    private const ushort COM1_MCR = COM1_BASE + 4;            // Modem Control Register
    private const ushort COM1_LSR = COM1_BASE + 5;            // Line Status Register

    // Line Status Register bits
    private const byte LSR_TX_EMPTY = 0x20;                   // Transmit buffer empty

    // Line Control Register values
    private const byte LCR_DLAB_ENABLE = 0x80;                // Divisor Latch Access Bit
    private const byte LCR_8N1 = 0x03;                        // 8 data bits, no parity, 1 stop bit

    // FIFO Control Register values
    private const byte FCR_ENABLE_FIFO = 0xC7;                // Enable FIFO, clear, 14-byte threshold

    // Modem Control Register values
    private const byte MCR_RTS_DSR = 0x0B;                    // RTS/DSR set, IRQs enabled

    // Baud rate divisor (115200 baud)
    private const byte BAUD_DIVISOR_LO = 0x01;
    private const byte BAUD_DIVISOR_HI = 0x00;

    private static void WaitForTransmitBufferEmpty()
    {
        while ((Native.IO.Read8(COM1_LSR) & LSR_TX_EMPTY) == 0) ;
    }

    public static void ComWrite(byte value)
    {
#if ARCH_ARM64
        // Wait for the PL011 TX FIFO to have space
        WaitForARM64TransmitReady();
#else
        // Wait for the transmit buffer to be empty (x86)
        WaitForTransmitBufferEmpty();
#endif
        // Write the byte to the COM port
        Native.IO.Write8(COM1_DATA, value);
    }

#if ARCH_ARM64
    private static void WaitForARM64TransmitReady()
    {
        // Wait until TX FIFO is not full
        while ((Native.IO.ReadDWord(UART0_BASE + UARTFR) & UARTFR_TXFF) != 0) ;
    }
#endif

    /// <summary>
    /// Initialize serial port - called from managed Kernel.Initialize()
    /// </summary>
    public static void ComInit()
    {
#if ARCH_ARM64
        // ARM64: Initialize PL011 UART
        InitializeARM64Serial();
#else
        // x86-64: Initialize COM1 serial port
        InitializeX64Serial();
#endif
    }

#if ARCH_ARM64
    // ARM64 PL011 UART Register Addresses (QEMU virt machine)
    private const ulong UART0_BASE = 0x09000000;              // PL011 UART0 base address
    private const ulong UARTDR = 0x00;                        // Data Register
    private const ulong UARTFR = 0x18;                        // Flag Register
    private const ulong UARTIBRD = 0x24;                      // Integer Baud Rate Divisor
    private const ulong UARTFBRD = 0x28;                      // Fractional Baud Rate Divisor
    private const ulong UARTLCR_H = 0x2C;                     // Line Control Register
    private const ulong UARTCR = 0x30;                        // Control Register
    private const ulong UARTIMSC = 0x38;                      // Interrupt Mask Set/Clear

    // PL011 Flag Register bits
    private const uint UARTFR_TXFF = 1 << 5;                  // TX FIFO Full

    // PL011 Line Control Register bits
    private const uint UARTLCR_H_FEN = 1 << 4;                // FIFO Enable
    private const uint UARTLCR_H_WLEN_8 = 3 << 5;             // 8-bit word length

    // PL011 Control Register bits
    private const uint UARTCR_UARTEN = 1 << 0;                // UART Enable
    private const uint UARTCR_TXE = 1 << 8;                   // Transmit Enable
    private const uint UARTCR_RXE = 1 << 9;                   // Receive Enable

    // Baud rate divisor for 115200 baud (24MHz clock)
    // Divisor = 24000000 / (16 * 115200) = 13.02
    private const uint UART_IBRD_115200 = 13;
    private const uint UART_FBRD_115200 = 1;

    private static void InitializeARM64Serial()
    {
        // Disable UART
        Native.IO.WriteDWord(UART0_BASE + UARTCR, 0);

        // Clear all interrupts
        Native.IO.WriteDWord(UART0_BASE + UARTIMSC, 0);

        // Set baud rate to 115200 (24MHz clock)
        Native.IO.WriteDWord(UART0_BASE + UARTIBRD, UART_IBRD_115200);
        Native.IO.WriteDWord(UART0_BASE + UARTFBRD, UART_FBRD_115200);

        // Enable FIFO, 8-bit data, no parity, 1 stop bit
        Native.IO.WriteDWord(UART0_BASE + UARTLCR_H, UARTLCR_H_FEN | UARTLCR_H_WLEN_8);

        // Enable UART, TX, and RX
        Native.IO.WriteDWord(UART0_BASE + UARTCR, UARTCR_UARTEN | UARTCR_TXE | UARTCR_RXE);
    }
#else
    private static void InitializeX64Serial()
    {
        // Disable all interrupts
        Native.IO.Write8(COM1_IER, 0x00);

        // Enable DLAB (set baud rate divisor)
        Native.IO.Write8(COM1_LCR, LCR_DLAB_ENABLE);

        // Set divisor for 115200 baud
        Native.IO.Write8(COM1_DATA, BAUD_DIVISOR_LO);
        Native.IO.Write8(COM1_IER, BAUD_DIVISOR_HI);

        // 8 bits, no parity, one stop bit
        Native.IO.Write8(COM1_LCR, LCR_8N1);

        // Enable FIFO, clear them, with 14-byte threshold
        Native.IO.Write8(COM1_FCR, FCR_ENABLE_FIFO);

        // IRQs enabled, RTS/DSR set
        Native.IO.Write8(COM1_MCR, MCR_RTS_DSR);
    }
#endif

    public static unsafe void WriteString(string str)
    {
        fixed (char* ptr = str)
        {
            for (int i = 0; i < str.Length; i++)
            {
                ComWrite((byte)ptr[i]);
            }
        }
    }

    public static unsafe void WriteNumber(ulong number, bool hex = false)
    {
        if (number == 0)
        {
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

    private const string NULL = "null";
    private const string TRUE = "TRUE";
    private const string FALSE = "FALSE";
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
                        WriteNumber((ulong)@byteArray[i], true);
                    }
                    break;
                default:
                    WriteString(args[i].ToString());
                    break;
            }
        }
    }
}
