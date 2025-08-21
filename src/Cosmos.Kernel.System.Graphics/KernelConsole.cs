namespace Cosmos.Kernel.System.Graphics;

public static class KernelConsole
{
    private static int _cursorX;
    private static int _cursorY;
    private const int CharWidth = 16;
    private const int CharHeight = 16;

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
        _cursorY += CharHeight;
        if (_cursorY + CharHeight > Canvas.Height)
        {
            Canvas.ClearScreen(Color.Black);
            _cursorY = 0;
        }
    }
}
