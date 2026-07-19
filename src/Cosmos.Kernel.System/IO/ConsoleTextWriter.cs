using System.Globalization;
using System.Text;
using Cosmos.Kernel.System.Graphics;

namespace Cosmos.Kernel.System.IO;

public sealed class ConsoleTextWriter : TextWriter
{
    public override Encoding Encoding => Encoding.Default;
    public override void Write(char value)
    {
        KernelConsole.Default.Write(value);
        if (KernelConsole.Default.IsAvailable)
        {
            KernelConsole.Default.Canvas.Display();
        }
    }
    public override void Write(string? value)
    {
        if (value is null)
        {
            return;
        }

        KernelConsole.Default.Write(value);
        if (KernelConsole.Default.IsAvailable)
        {
            KernelConsole.Default.Canvas.Display();
        }
    }

    public override void Write(ReadOnlySpan<char> buffer)
    {
        KernelConsole.Default.Write(buffer);
        if (KernelConsole.Default.IsAvailable)
        {
            KernelConsole.Default.Canvas.Display();
        }
    }
}
