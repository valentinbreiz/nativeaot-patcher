using Cosmos.Build.API.Attributes;
using Cosmos.Kernel.System.Graphics;

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
}
