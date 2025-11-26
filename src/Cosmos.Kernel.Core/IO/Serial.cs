using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.Runtime;

namespace Cosmos.Kernel.Core.IO;

public static class Serial
{
    private static readonly ushort COM1 = 0x3F8;

    private static void WaitForTransmitBufferEmpty()
    {
        while ((Native.IO.Read8((ushort)(COM1 + 5)) & 0x20) == 0) ;
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
        Native.IO.Write8(COM1, value);
    }

#if ARCH_ARM64
    private static void WaitForARM64TransmitReady()
    {
        // PL011 UARTFR (Flag Register) at offset 0x18
        // Bit 5 (TXFF) = TX FIFO Full flag (1 = full, 0 = not full)
        while ((Native.IO.ReadDWord(UART0_BASE + UARTFR) & (1 << 5)) != 0) ;
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
    // ARM64 PL011 UART constants
    private const ulong UART0_BASE = 0x09000000;
    private const ulong UARTDR = 0x00;
    private const ulong UARTFR = 0x18;
    private const ulong UARTIBRD = 0x24;
    private const ulong UARTFBRD = 0x28;
    private const ulong UARTLCR_H = 0x2C;
    private const ulong UARTCR = 0x30;
    private const ulong UARTIMSC = 0x38;

    private static void InitializeARM64Serial()
    {
        // Disable UART
        Native.IO.WriteDWord(UART0_BASE + UARTCR, 0);

        // Clear all interrupts
        Native.IO.WriteDWord(UART0_BASE + UARTIMSC, 0);

        // Set baud rate to 115200 (24MHz clock)
        // Divisor = 24000000 / (16 * 115200) = 13.02
        Native.IO.WriteDWord(UART0_BASE + UARTIBRD, 13);
        Native.IO.WriteDWord(UART0_BASE + UARTFBRD, 1);

        // Enable FIFO, 8-bit data, no parity, 1 stop bit
        Native.IO.WriteDWord(UART0_BASE + UARTLCR_H, (1 << 4) | (3 << 5));

        // Enable UART, TX, and RX
        Native.IO.WriteDWord(UART0_BASE + UARTCR, (1 << 0) | (1 << 8) | (1 << 9));
    }
#else
    private static void InitializeX64Serial()
    {
        // Disable all interrupts
        Native.IO.Write8((ushort)(COM1 + 1), 0x00);

        // Enable DLAB (set baud rate divisor)
        Native.IO.Write8((ushort)(COM1 + 3), 0x80);

        // Set divisor to 1 (lo byte) 115200 baud
        Native.IO.Write8(COM1, 0x01);

        // Set divisor to 1 (hi byte)
        Native.IO.Write8((ushort)(COM1 + 1), 0x00);

        // 8 bits, no parity, one stop bit
        Native.IO.Write8((ushort)(COM1 + 3), 0x03);

        // Enable FIFO, clear them, with 14-byte threshold
        Native.IO.Write8((ushort)(COM1 + 2), 0xC7);

        // IRQs enabled, RTS/DSR set
        Native.IO.Write8((ushort)(COM1 + 4), 0x0B);
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
