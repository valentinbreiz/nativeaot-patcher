using System;
using System.Text;
using Cosmos.Build.API.Attributes;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Input;

namespace Cosmos.Kernel.Plugs.System;

[Plug(typeof(Console))]
public class ConsolePlug
{
    [PlugMember]
    public static void Write(string value) => KernelConsole.Write(value);

    [PlugMember]
    public static void WriteLine(string value) => KernelConsole.WriteLine(value);

    [PlugMember]
    public static void Write(char value) => KernelConsole.Write(value);

    [PlugMember]
    public static void WriteLine(char value) => KernelConsole.WriteLine(value);

    [PlugMember]
    public static void WriteLine() => KernelConsole.WriteLine();

    [PlugMember]
    public static ConsoleKeyInfo ReadKey()
    {
        char c = KernelKeyboard.ReadChar();
        return new ConsoleKeyInfo(c, ConsoleKey.NoName, false, false, false);
    }

    [PlugMember]
    public static string ReadLine()
    {
        var buffer = new StringBuilder();
        while (true)
        {
            char c = KernelKeyboard.ReadChar();
            if (c == '\n' || c == '\r')
            {
                KernelConsole.WriteLine();
                break;
            }

            buffer.Append(c);
            KernelConsole.Write(c);
        }

        return buffer.ToString();
    }
}
