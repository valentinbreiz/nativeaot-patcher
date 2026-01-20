using System.Globalization;
using System.Text;
using Cosmos.Kernel.System.Graphics;

namespace Cosmos.Kernel.System.IO;

public sealed class ConsoleTextWriter : TextWriter
{
    public override Encoding Encoding => Encoding.Default;
    public override void Write(char value)
    {
        KernelConsole.Write(value);
        if (KernelConsole.IsAvailable)
        {
            KernelConsole.Canvas.Display();
        }
    }
    public override void Write(string? value)
    {
        if (value is null)
        {
            return;
        }

        KernelConsole.Write(value);
        if (KernelConsole.IsAvailable)
        {
            KernelConsole.Canvas.Display();
        }
    }

    public override void Write(ReadOnlySpan<char> buffer)
    {
        KernelConsole.Write(buffer);
        if (KernelConsole.IsAvailable)
        {
            KernelConsole.Canvas.Display();
        }
    }
}
