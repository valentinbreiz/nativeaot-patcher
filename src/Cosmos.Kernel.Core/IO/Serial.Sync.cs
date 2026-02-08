using System.Collections.Concurrent;
using Cosmos.Kernel.Core.Async;

namespace Cosmos.Kernel.Core.IO;


public static partial class Serial
{

    /// <summary>
    /// Write a single byte to the serial port.
    /// Does not wait.
    /// </summary>
    public static void ComWrite(byte value)
    {
        ComWriteAsync(value, (_) => { });
    }

    public static void WriteString(string str)
    {
        WriteStringAsync(str, (_) => { });
    }

    public static void WriteNumber(ulong number, bool hex = false)
    {
        WriteNumberAsync(number, hex, (_) => { });
    }

    public static void WriteNumber(uint number, bool hex = false)
    {
        WriteNumberAsync(number, hex, (_) => { });
    }

    public static void WriteNumber(int number, bool hex = false)
    {
        WriteNumberAsync(number, hex, (_) => { });
    }

    public static void WriteNumber(long number, bool hex = false)
    {
        WriteNumberAsync(number, hex, (_) => { });
    }

    public static void WriteHex(ulong number)
    {
        WriteHexAsync(number, (_) => { });
    }

    public static void WriteHex(uint number)
    {
        WriteHexAsync(number, (_) => { });
    }

    public static void WriteHexWithPrefix(ulong number)
    {
        WriteHexWithPrefixAsync(number, (_) => { });
    }

    public static void WriteHexWithPrefix(uint number)
    {
        WriteHexWithPrefixAsync(number, (_) => { });
    }

    public static void Write(params object?[] args)
    {
        WriteAsync(args.ToArray(), (_) => { });
    }

}
