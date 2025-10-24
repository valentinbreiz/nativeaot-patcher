using Cosmos.Kernel.Core.Runtime;

namespace Cosmos.Kernel.System.IO;

public static class Serial
{
    private static readonly ushort COM1 = 0x3F8;

    private static void WaitForTransmitBufferEmpty()
    {
        while ((Native.IO.Read8((ushort)(COM1 + 5)) & 0x20) == 0) ;
    }

    public static void ComWrite(byte value)
    {
        // Wait for the transmit buffer to be empty
        WaitForTransmitBufferEmpty();
        // Write the byte to the COM port
        Native.IO.Write8(COM1, value);
    }

    public static void ComInit()
    {
        Native.IO.Write8((ushort)(COM1 + 1), 0x00);
        Native.IO.Write8((ushort)(COM1 + 3), 0x80);
        Native.IO.Write8(COM1, 0x01);
        Native.IO.Write8((ushort)(COM1 + 1), 0x00);
        Native.IO.Write8((ushort)(COM1 + 3), 0x03);
        Native.IO.Write8((ushort)(COM1 + 2), 0xC7);
    }

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
