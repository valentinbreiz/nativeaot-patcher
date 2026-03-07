// This code is licensed under MIT license (see LICENSE for details)

using System.Diagnostics.CodeAnalysis;
using Cosmos.Kernel.Core.Async;

namespace Cosmos.Kernel.Core.IO;

public static partial class SerialAsync
{

    private static void RunAsync(Action work)
    {
        Enqueue(() =>
        {
            try
            {
                work();
            }
            catch (Exception e)
            {
            }
        });
    }

    public static void ComWrite(byte value) =>
        RunAsync(() => Serial.ComWrite(value));

    public static void WriteString(string str) =>
        RunAsync(() => Serial.WriteString(str));

    public static void WriteNumber(ulong number, bool hex) =>
        RunAsync(() => Serial.WriteNumber(number, hex));

    public static void WriteNumber(uint number, bool hex) =>
        RunAsync(() => Serial.WriteNumber(number, hex));

    public static void WriteNumber(int number, bool hex) =>
        RunAsync(() => Serial.WriteNumber(number, hex));

    public static void WriteNumber(long number, bool hex) =>
        RunAsync(() => Serial.WriteNumber(number, hex));

    public static void WriteHex(ulong number) =>
        RunAsync(() => Serial.WriteHex(number));

    public static void WriteHex(uint number) =>
        RunAsync(() => Serial.WriteHex(number));

    public static void WriteHexWithPrefix(ulong number) =>
        RunAsync(() => Serial.WriteHexWithPrefix(number));

    public static void WriteHexWithPrefix(uint number) =>
        RunAsync(() => Serial.WriteHexWithPrefix(number));

    public static void Write(params object?[] args) =>
        RunAsync(() => Serial.Write(args));

}
