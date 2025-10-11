namespace Cosmos.Kernel.System.Graphics;

public class Color
{
    public static uint Red = 0xFF0000;
    public static uint Green = 0x00FF00;
    public static uint Blue = 0x0000FF;
    public static uint White = 0xFFFFFF;
    public static uint Black = 0x000000;
    public static uint Yellow = 0xFFFF00;
    public static uint Cyan = 0x00FFFF;
    public static uint Magenta = 0xFF00FF;
    public static uint Orange = 0xFFA500;
    public static uint Purple = 0x800080;
    public static uint Pink = 0xFFC0CB;
    public static uint Brown = 0xA52A2A;
    public static uint Gray = 0x808080;
    public static uint LightGray = 0xD3D3D3;
    public static uint DarkGray = 0xA9A9A9;
    public static uint LightRed = 0xFF7F7F;
    public static uint LightGreen = 0x7FFF7F;
    public static uint LightBlue = 0x7F7FFF;
    public static uint LightYellow = 0xFFFF7F;
    public static uint LightCyan = 0x7FFFFF;
    public static uint LightMagenta = 0xFF7FFF;
    public static uint LightOrange = 0xFFBF00;
    public static uint LightPurple = 0xBF00FF;
    public static uint LightPink = 0xFFB6C1;
    public static uint LightBrown = 0xD2B48C;
    public static uint Transparent = 0x00000000; // Transparent color

    public static uint Blend(uint color1, uint color2, float ratio)
    {
        if (ratio < 0) ratio = 0;
        if (ratio > 1) ratio = 1;

        byte r1 = (byte)((color1 >> 16) & 0xFF);
        byte g1 = (byte)((color1 >> 8) & 0xFF);
        byte b1 = (byte)(color1 & 0xFF);

        byte r2 = (byte)((color2 >> 16) & 0xFF);
        byte g2 = (byte)((color2 >> 8) & 0xFF);
        byte b2 = (byte)(color2 & 0xFF);

        byte r = (byte)(r1 * (1 - ratio) + r2 * ratio);
        byte g = (byte)(g1 * (1 - ratio) + g2 * ratio);
        byte b = (byte)(b1 * (1 - ratio) + b2 * ratio);

        return (uint)((r << 16) | (g << 8) | b);
    }
}
