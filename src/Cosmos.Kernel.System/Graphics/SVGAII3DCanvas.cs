using System;
using System.Collections.Generic;
using System.Drawing;
using Cosmos.Kernel.HAL.Devices.Graphic.SVGAII;
using Cosmos.Kernel.HAL.Devices.Graphic;
using Cosmos.Kernel.System.Graphics.Fonts;

namespace Cosmos.Kernel.System.Graphics;

/// <summary>
/// Defines a VMWare SVGAII canvas implementation.
/// Please note that this implementation of <see cref="Canvas"/> can only be
/// used with virtualizers that implement SVGAII. This class will not work on
/// regular hardware.
/// </summary>
public class SVGAII3DCanvas : Canvas
{
    private static readonly Mode s_defaultMode = new(1024, 768, ColorDepth.ColorDepth32);

    private int _bytesPerPixel;
    private int _pitch;
    private int _stride;
    private Mode _mode;

    public VMWareSVGAII3D Driver { get; }

    public SVGAII3DCanvas()
        : this(s_defaultMode)
    {
    }

    public SVGAII3DCanvas(Mode aMode)
        : base(aMode)
    {
        ThrowIfModeIsNotValid(aMode);

        Driver = new VMWareSVGAII3D();
        Mode = aMode;
    }

    public override string Name() => "VMWareSVGAII";

    /// <summary>
    /// Gets or sets the current graphics mode.
    /// </summary>
    public override Mode Mode
    {
        get => _mode;
        set
        {
            ThrowIfModeIsNotValid(value);
            _mode = value;
            SetGraphicsMode(_mode);
            _bytesPerPixel = (int)_mode.ColorDepth / 8;
            _stride = _bytesPerPixel;
            _pitch = (int)_mode.Width * _bytesPerPixel;
        }
    }

    public override Mode DefaultGraphicsMode => s_defaultMode;

    public override void Disable()
    {
        Driver.Disable();
    }

    public override void DrawPoint(Color color, int x, int y)
    {
        if (color.A < 255)
        {
            if (color.A == 0)
            {
                return;
            }

            color = AlphaBlend(color, GetPointColor(x, y), color.A);
        }

        Driver.SetPixel((uint)x, (uint)y, (uint)color.ToArgb());
    }

    public override void DrawPoint(uint color, int x, int y)
    {
        Driver.SetPixel((uint)x, (uint)y, color);
    }

    public override void DrawPoint(int color, int x, int y)
    {
        Driver.SetPixel((uint)x, (uint)y, (uint)color);
    }

    public override void DrawArray(Color[] colors, int x, int y, int width, int height)
    {
        ThrowIfCoordNotValid(x, y);
        ThrowIfCoordNotValid(x + width - 1, y + height - 1);

        for (int row = 0; row < height; row++)
        {
            for (int column = 0; column < width; column++)
            {
                DrawPoint(colors[column + (row * width)], x + column, y + row);
            }
        }
    }

    internal new int GetPointOffset(int x, int y)
    {
        return (x * _stride) + (y * _pitch);
    }

    public override void DrawArray(int[] colors, int x, int y, int width, int height)
    {
        int frameSize = (int)Driver.FrameSize;

        for (int row = 0; row < height; row++)
        {
            Driver.VideoMemory.Copy(GetPointOffset(x, y + row) + frameSize, colors, row * width, width);
        }
    }

    public override void DrawArray(int[] colors, int x, int y, int width, int height, int startIndex)
    {
        int frameSize = (int)Driver.FrameSize;

        for (int row = 0; row < height; row++)
        {
            Driver.VideoMemory.Copy(GetPointOffset(x, y + row) + frameSize, colors, row * width + startIndex, width);
        }
    }

    public override void DrawFilledRectangle(Color color, int xStart, int yStart, int width, int height, bool preventOffBoundPixels = true)
    {
        int argb = color.ToArgb();
        int frameSize = (int)Driver.FrameSize;

        if (preventOffBoundPixels)
        {
            width = Math.Min(width, (int)_mode.Width - xStart);
            height = Math.Min(height, (int)_mode.Height - yStart);
        }

        for (int row = yStart; row < yStart + height; row++)
        {
            Driver.VideoMemory.Fill(GetPointOffset(xStart, row) + frameSize, width, argb);
        }
    }

    public override void DrawRectangle(Color color, int x, int y, int width, int height)
    {
        if (color.A < 255)
        {
            DrawLine(color, x, y, x + width, y);
            DrawLine(color, x, y, x, y + height);
            DrawLine(color, x, y + height, x + width, y + height);
            DrawLine(color, x + width, y, x + width, y + height);
            return;
        }

        int rawColor = color.ToArgb();
        int bottomY = y + height;
        int rightX = x + width;

        for (int posX = x; posX < rightX; posX++)
        {
            DrawPoint((uint)rawColor, posX, y);
            DrawPoint((uint)rawColor, posX, bottomY);
        }

        for (int posY = y; posY < bottomY; posY++)
        {
            DrawPoint((uint)rawColor, x, posY);
            DrawPoint((uint)rawColor, rightX, posY);
        }
    }

    public override List<Mode> AvailableModes { get; } = new List<Mode>
    {
        /* VmWare may support 16-bit resolutions but CGS does not yet.
           That would require RGB32->RGB16 conversion. */
        new Mode(320, 200, ColorDepth.ColorDepth32),
        new Mode(320, 240, ColorDepth.ColorDepth32),
        new Mode(640, 480, ColorDepth.ColorDepth32),
        new Mode(720, 480, ColorDepth.ColorDepth32),
        new Mode(800, 600, ColorDepth.ColorDepth32),
        new Mode(1024, 768, ColorDepth.ColorDepth32),
        new Mode(1152, 768, ColorDepth.ColorDepth32),
        new Mode(1280, 720, ColorDepth.ColorDepth32),
        new Mode(1280, 768, ColorDepth.ColorDepth32),
        new Mode(1280, 800, ColorDepth.ColorDepth32),
        new Mode(1280, 1024, ColorDepth.ColorDepth32),
        new Mode(1360, 768, ColorDepth.ColorDepth32),
        // new Mode(1366, 768, ColorDepth.ColorDepth32), // Original laptop resolution; broken in VMware.
        new Mode(1440, 900, ColorDepth.ColorDepth32),
        new Mode(1400, 1050, ColorDepth.ColorDepth32),
        new Mode(1600, 1200, ColorDepth.ColorDepth32),
        new Mode(1680, 1050, ColorDepth.ColorDepth32),
        new Mode(1920, 1080, ColorDepth.ColorDepth32),
        new Mode(1920, 1200, ColorDepth.ColorDepth32),
        new Mode(2048, 1536, ColorDepth.ColorDepth32),
        new Mode(2560, 1080, ColorDepth.ColorDepth32),
        new Mode(2560, 1600, ColorDepth.ColorDepth32),
        new Mode(2560, 2048, ColorDepth.ColorDepth32),
        new Mode(3200, 2048, ColorDepth.ColorDepth32),
        new Mode(3200, 2400, ColorDepth.ColorDepth32),
        new Mode(3840, 2400, ColorDepth.ColorDepth32),
    };

    private void SetGraphicsMode(Mode mode)
    {
        ThrowIfModeIsNotValid(mode);

        uint width = (uint)mode.Width;
        uint height = (uint)mode.Height;
        uint colorDepth = (uint)mode.ColorDepth;

        Driver.SetMode(width, height, colorDepth);
    }

    public override void Clear(int color)
    {
        Driver.Clear((uint)color);
    }

    public override void Clear(Color color)
    {
        Driver.Clear((uint)color.ToArgb());
    }

    public Color GetPixel(int x, int y)
    {
        uint argb = Driver.GetPixel((uint)x, (uint)y);
        return Color.FromArgb((int)argb);
    }

    public void SetCursor(bool visible, int x, int y)
    {
        Driver.SetCursor(visible, (uint)x, (uint)y);
    }

    public void CreateCursor()
    {
        Driver.DefineCursor();
    }

    public void CopyPixels(int srcX, int srcY, int dstX, int dstY, int width = 1, int height = 1)
    {
        Driver.Copy((uint)srcX, (uint)srcY, (uint)dstX, (uint)dstY, (uint)width, (uint)height);
    }

    public void MovePixel(int x, int y, int newX, int newY)
    {
        Driver.Copy((uint)x, (uint)y, (uint)newX, (uint)newY, 1, 1);
        Driver.SetPixel((uint)x, (uint)y, 0);
    }

    public override Color GetPointColor(int x, int y)
    {
        return Color.FromArgb((int)Driver.GetPixel((uint)x, (uint)y));
    }

    public override int GetRawPointColor(int x, int y)
    {
        return (int)Driver.GetPixel((uint)x, (uint)y);
    }

    public override Bitmap GetImage(int x, int y, int width, int height)
    {
        int frameSize = (int)Driver.FrameSize;
        int[] buffer = new int[width];
        int[] all = new int[width * height];

        for (int row = 0; row < height; row++)
        {
            Driver.VideoMemory.Get(GetPointOffset(x, y + row) + frameSize, buffer, 0, width);
            buffer.CopyTo(all, width * row);
        }

        Bitmap bitmap = new Bitmap((uint)width, (uint)height, ColorDepth.ColorDepth32)
        {
            RawData = all,
        };

        return bitmap;
    }

    public override void Display()
    {
        Driver.DoubleBufferUpdate();
    }

    public override void DrawString(string str, Font font, Color color, int x, int y)
    {
        int length = str.Length;
        int width = font.Width;

        for (int i = 0; i < length; i++)
        {
            DrawChar(str[i], font, color, x, y);
            x += width;
        }
    }

    public override void DrawChar(char c, Font font, Color color, int x, int y)
    {
        int height = font.Height;
        int width = font.Width;
        byte[] data = font.Data;
        int p = height * c;

        for (int cy = 0; cy < height; cy++)
        {
            for (int cx = 0; cx < width; cx++)
            {
                if (font.ConvertByteToBitAddress(data[p + cy], cx + 1))
                {
                    DrawPoint(color, x + cx, y + cy);
                }
            }
        }
    }

    public override void DrawImage(Image image, int x, int y, bool preventOffBoundPixels = true)
    {
        int width = (int)image.Width;
        int height = (int)image.Height;
        int frameSize = (int)Driver.FrameSize;
        int[] data = image.RawData;

        if (preventOffBoundPixels)
        {
            int maxWidth = Math.Min(width, (int)_mode.Width - x);
            int maxHeight = Math.Min(height, (int)_mode.Height - y);
            int startX = Math.Max(0, x);
            int startY = Math.Max(0, y);
            int sourceX = Math.Max(0, -x);
            int sourceY = Math.Max(0, -y);

            maxWidth -= startX - x;
            maxHeight -= startY - y;

            for (int row = 0; row < maxHeight; row++)
            {
                int sourceIndex = (sourceY + row) * width + sourceX;
                Driver.VideoMemory.Copy(GetPointOffset(startX, startY + row) + frameSize, data, sourceIndex, maxWidth);
            }
        }
        else
        {
            for (int row = 0; row < height; row++)
            {
                Driver.VideoMemory.Copy(GetPointOffset(x, y + row) + frameSize, data, row * width, width);
            }
        }
    }

    public override void CroppedDrawImage(Image image, int x, int y, int width, int height, bool preventOffBoundPixels = true)
    {
        int frameSize = (int)Driver.FrameSize;
        int[] data = image.RawData;

        if (preventOffBoundPixels)
        {
            int modeWidth = (int)_mode.Width;
            int modeHeight = (int)_mode.Height;
            int maxWidth = Math.Min(width, modeWidth - x);
            int maxHeight = Math.Min(height, modeHeight - y);
            int startX = Math.Max(0, -x);
            int startY = Math.Max(0, -y);
            int sourceWidth = maxWidth - startX;
            int sourceHeight = maxHeight - startY;

            for (int row = 0; row < sourceHeight; row++)
            {
                int destY = y + startY + row;
                int destOffset = GetPointOffset(x + startX, destY) + frameSize;
                Driver.VideoMemory.Copy(destOffset, data, (startY + row) * width + startX, sourceWidth);
            }
        }
        else
        {
            for (int row = 0; row < height; row++)
            {
                Driver.VideoMemory.Copy(GetPointOffset(x, y + row) + frameSize, data, row * width, width);
            }
        }
    }
}
