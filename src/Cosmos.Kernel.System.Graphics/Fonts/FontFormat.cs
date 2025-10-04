namespace Cosmos.Kernel.System.Graphics.Fonts;

public abstract class FontFormat
{
    public abstract int CharWidth { get; }
    public abstract int CharHeight { get; }
    public abstract void PutChar(char c, int x, int y, uint fgcolor, uint bgcolor);
    public abstract void PutString(string str, int x, int y, uint fgcolor, uint bgcolor);
    public abstract unsafe void PutString(char* str, int x, int y, uint fgcolor, uint bgcolor);
}