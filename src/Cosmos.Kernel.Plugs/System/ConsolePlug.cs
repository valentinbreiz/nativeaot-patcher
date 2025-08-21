using System;
using Cosmos.Build.API.Attributes;
using Cosmos.Kernel.System.Graphics;

namespace Cosmos.Kernel.Plugs.System;

[Plug(typeof(Console))]
public static class ConsolePlug
{
    public static void Write(string value) => KernelConsole.Write(value);
    public static void WriteLine(string value) => KernelConsole.WriteLine(value);
    public static void Write(char value) => KernelConsole.Write(value);
    public static void WriteLine(char value) => KernelConsole.WriteLine(value);
    public static void WriteLine() => KernelConsole.WriteLine();
}
