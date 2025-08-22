using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.System.Graphics.Fonts;

namespace Cosmos.Kernel.System.Graphics;

public static class KernelConsole
{
    private static int _cursorX;
    private static int _cursorY;
    private static int CharWidth => PCScreenFont.CharWidth;
    private static int CharHeight => PCScreenFont.CharHeight;
    private const int LineSpacing = 0;

    public static void Write(string text)
    {
        foreach (char c in text)
        {
            Write(c);
        }
    }

    public static void Write(char c)
    {
        if (c == '\n')
        {
            NewLine();
            return;
        }

        Canvas.DrawChar(c, _cursorX, _cursorY, Color.White);
        _cursorX += CharWidth;
        if (_cursorX + CharWidth > Canvas.Width)
        {
            NewLine();
        }
    }

    public static void WriteLine(string text)
    {
        Write(text);
        NewLine();
    }

    public static void WriteLine(char c)
    {
        Write(c);
        NewLine();
    }

    public static void WriteLine()
    {
        NewLine();
    }

    private static void NewLine()
    {
        _cursorX = 0;
        _cursorY += CharHeight + LineSpacing;
        if (_cursorY + CharHeight > Canvas.Height)
        {
            Scroll();
        }
    }

    private static unsafe void Scroll()
    {
        int lineHeight = CharHeight + LineSpacing;
        int lineSize = lineHeight * (int)Canvas.Pitch;
        int screenSize = (int)(Canvas.Pitch * Canvas.Height);
        MemoryOp.MemMove((byte*)Canvas.Address, (byte*)Canvas.Address + lineSize, screenSize - lineSize);
        MemoryOp.MemSet((uint*)((byte*)Canvas.Address + screenSize - lineSize), Color.Black, (int)((Canvas.Pitch / 4) * lineHeight));
        _cursorY = (int)Canvas.Height - CharHeight - LineSpacing;
    }
}
