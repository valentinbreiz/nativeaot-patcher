using System.Diagnostics.CodeAnalysis;
using System.Text;
using Cosmos.Kernel.Core.Async;

namespace Cosmos.Kernel.Core.IO;

public static partial class SerialAsync
{
    public static void ComWrite(byte value) =>
        RunAsync(() => Serial.ComWrite(value), (_) => { });

    public static void WriteString(string str) =>
        RunAsync(() => Serial.WriteString(str), (_) => { });

    public static void WriteNumber(ulong number, bool hex) =>
        RunAsync(() => Serial.WriteNumber(number, hex), (_) => { });

    public static void WriteNumber(uint number, bool hex) =>
        RunAsync(() => Serial.WriteNumber(number, hex), (_) => { });

    public static void WriteNumber(int number, bool hex) =>
        RunAsync(() => Serial.WriteNumber(number, hex), (_) => { });

    public static void WriteNumber(long number, bool hex) =>
        RunAsync(() => Serial.WriteNumber(number, hex), (_) => { });

    public static void WriteHex(ulong number) =>
        RunAsync(() => Serial.WriteHex(number), (_) => { });

    public static void WriteHex(uint number) =>
        RunAsync(() => Serial.WriteHex(number), (_) => { });

    public static void WriteHexWithPrefix(ulong number) =>
        RunAsync(() => Serial.WriteHexWithPrefix(number), (_) => { });

    public static void WriteHexWithPrefix(uint number) =>
        RunAsync(() => Serial.WriteHexWithPrefix(number), (_) => { });

    public static void Write(object?[] args) =>
        RunAsync(() => Serial.Write(args), (_) => { });
}
