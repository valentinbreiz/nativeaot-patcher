namespace Cosmos.Kernel.System.Graphics.Fonts;

public abstract class FontFormat
{
    public abstract int CharWidth { get; }
    public abstract int CharHeight { get; }
    public abstract void PutChar(char c, int x, int y, uint fgcolor, uint bgcolor);
    public abstract void PutString(string str, int x, int y, uint fgcolor, uint bgcolor);
    public abstract unsafe void PutString(char* str, int x, int y, uint fgcolor, uint bgcolor);
    public virtual void PutScaledChar(char c, int x, int y, uint fgcolor, uint bgcolor, int scale) {}
    public virtual void PutScaledString(string str, int x, int y, uint fgcolor, uint bgcolor, int scale)
    {
        for (int i = 0; i < str.Length; i++)
        {
            PutScaledChar(str[i], x, y, fgcolor, bgcolor, scale);
        }
    }
}