using System;
using System.Collections.Generic;
using System.Drawing;
using Cosmos.Kernel.HAL.Devices.Graphic.SVGAII;

namespace Cosmos.Kernel.System.Graphics;

/// <summary>
/// 2D drawing routines shared by <see cref="SvgaIICanvas"/> and
/// <see cref="SvgaII3DCanvas"/>. Single inheritance keeps the 3D canvas from
/// deriving from the 2D one (it derives from <see cref="Canvas3D"/>), so both
/// delegate their non-trivial driver-backed operations here.
/// </summary>
internal static class SvgaIIRender
{
    /// <summary>
    /// The default graphics mode of the SVGA II canvases.
    /// </summary>
    public static readonly Mode DefaultMode = new(1024, 768, ColorDepth.ColorDepth32);

    /// <summary>
    /// The graphics modes supported by the SVGA II canvases.
    /// </summary>
    public static List<Mode> CreateAvailableModes() => new()
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

    /// <summary>
    /// Programs the device with the given mode and refreshes the canvas's
    /// pixel-layout metrics.
    /// </summary>
    public static void ApplyMode(Canvas canvas, SvgaIIDriver driver, Mode mode)
    {
        driver.SetMode((uint)mode.Width, (uint)mode.Height, (uint)mode.ColorDepth);

        canvas._bytesPerPixel = (int)mode.ColorDepth / 8;
        canvas._stride = canvas._bytesPerPixel;
        canvas._pitch = mode.Width * canvas._bytesPerPixel;
    }

    /// <summary>
    /// Draws an alpha-blended point through the device.
    /// </summary>
    public static void DrawPoint(Canvas canvas, SvgaIIDriver driver, Color color, int x, int y)
    {
        if (x < 0 || x >= canvas.Width || y < 0 || y >= canvas.Height)
        {
            return;
        }

        if (color.A < 255)
        {
            if (color.A == 0)
            {
                return;
            }

            color = Canvas.AlphaBlend(color, canvas.GetPointColor(x, y), color.A);
        }

        driver.DrawPixel((uint)color.ToArgb(), x, y);
    }

    /// <summary>
    /// Draws a raw ARGB point through the device, clipped to the canvas.
    /// </summary>
    public static void DrawRawPoint(Canvas canvas, SvgaIIDriver driver, uint color, int x, int y)
    {
        if (x < 0 || x >= canvas.Width || y < 0 || y >= canvas.Height)
        {
            return;
        }

        driver.DrawPixel(color, x, y);
    }

    /// <summary>
    /// Draws an array of colors point by point through the canvas.
    /// </summary>
    public static void DrawArray(Canvas canvas, Color[] colors, int x, int y, int width, int height)
    {
        for (int row = 0; row < height; row++)
        {
            for (int column = 0; column < width; column++)
            {
                canvas.DrawPoint(colors[column + (row * width)], x + column, y + row);
            }
        }
    }

    /// <summary>
    /// Fills a rectangle through the device's VRAM fill operation.
    /// </summary>
    public static void DrawFilledRectangle(Canvas canvas, SvgaIIDriver driver, Color color, int xStart, int yStart, int width, int height, bool preventOffBoundPixels)
    {
        int argb = color.ToArgb();

        if (preventOffBoundPixels)
        {
            width = Math.Min(width, canvas.Width - xStart);
            height = Math.Min(height, canvas.Height - yStart);
        }

        for (int row = yStart; row < yStart + height; row++)
        {
            driver.ClearVRAM(canvas.GetPointOffset(xStart, row), width, argb);
        }
    }

    /// <summary>
    /// Draws a rectangle outline through the canvas's point and line
    /// primitives.
    /// </summary>
    public static void DrawRectangle(Canvas canvas, Color color, int x, int y, int width, int height)
    {
        if (color.A < 255)
        {
            canvas.DrawLine(color, x, y, x + width, y);
            canvas.DrawLine(color, x, y, x, y + height);
            canvas.DrawLine(color, x, y + height, x + width, y + height);
            canvas.DrawLine(color, x + width, y, x + width, y + height);
            return;
        }

        int rawColor = color.ToArgb();
        int bottomY = y + height;
        int rightX = x + width;

        for (int posX = x; posX < rightX; posX++)
        {
            canvas.DrawPoint((uint)rawColor, posX, y);
            canvas.DrawPoint((uint)rawColor, posX, bottomY);
        }

        for (int posY = y; posY < bottomY; posY++)
        {
            canvas.DrawPoint((uint)rawColor, x, posY);
            canvas.DrawPoint((uint)rawColor, rightX, posY);
        }
    }

    /// <summary>
    /// Copies a rectangle of pixels using the device's accelerated copy
    /// operation. See <see cref="Canvas.CopyPixels"/> for the clipping and
    /// overlap contract.
    /// </summary>
    public static void CopyPixels(Canvas canvas, SvgaIIDriver driver, int srcX, int srcY, int dstX, int dstY, int width, int height)
    {
        int left = Math.Max(0, Math.Max(-srcX, -dstX));
        int top = Math.Max(0, Math.Max(-srcY, -dstY));
        int right = Math.Min(width, Math.Min(canvas.Width - srcX, canvas.Width - dstX));
        int bottom = Math.Min(height, Math.Min(canvas.Height - srcY, canvas.Height - dstY));

        if (left >= right || top >= bottom)
        {
            return;
        }

        driver.Copy((uint)(srcX + left), (uint)(srcY + top), (uint)(dstX + left), (uint)(dstY + top),
            (uint)(right - left), (uint)(bottom - top));
    }

    /// <summary>
    /// Reads a rectangle of pixels back from VRAM into a bitmap.
    /// </summary>
    public static Bitmap GetImage(Canvas canvas, SvgaIIDriver driver, int x, int y, int width, int height)
    {
        int[] all = new int[width * height];

        for (int row = 0; row < height; row++)
        {
            driver.GetVRAM(canvas.GetPointOffset(x, y + row), all, width * row, width);
        }

        Bitmap bitmap = new Bitmap(width, height, ColorDepth.ColorDepth32)
        {
            RawData = all,
        };

        return bitmap;
    }

    /// <summary>
    /// Draws an image through the device's buffer copy operation.
    /// </summary>
    public static void DrawImage(Canvas canvas, SvgaIIDriver driver, Image image, int x, int y, bool preventOffBoundPixels)
    {
        int width = image.Width;
        int height = image.Height;
        int[] data = image.RawData;

        if (preventOffBoundPixels)
        {
            int maxWidth = Math.Min(width, canvas.Width - x);
            int maxHeight = Math.Min(height, canvas.Height - y);
            int startX = Math.Max(0, x);
            int startY = Math.Max(0, y);
            int sourceX = Math.Max(0, -x);
            int sourceY = Math.Max(0, -y);

            maxWidth -= startX - x;
            maxHeight -= startY - y;

            if (maxWidth <= 0 || maxHeight <= 0)
            {
                return;
            }

            if (sourceX == 0 && sourceY == 0 && maxWidth == width && maxHeight == height)
            {
                driver.CopyBuffer(data.AsMemory(), startX, startY, width, height);
            }
            else
            {
                // Copy row by row due to the source offset
                for (int row = 0; row < maxHeight; row++)
                {
                    int sourceIndex = (sourceY + row) * width + sourceX;
                    driver.CopyBuffer(data.AsMemory(sourceIndex, maxWidth), startX, startY + row, maxWidth, 1);
                }
            }
        }
        else
        {
            driver.CopyBuffer(data.AsMemory(), x, y, width, height);
        }
    }

    /// <summary>
    /// Draws a cropped image through the device's buffer copy operation.
    /// </summary>
    public static void CroppedDrawImage(Canvas canvas, SvgaIIDriver driver, Image image, int x, int y, int width, int height, bool preventOffBoundPixels)
    {
        int[] data = image.RawData;

        if (preventOffBoundPixels)
        {
            int maxWidth = Math.Min(width, canvas.Width - x);
            int maxHeight = Math.Min(height, canvas.Height - y);
            int startX = Math.Max(0, -x);
            int startY = Math.Max(0, -y);
            int sourceWidth = maxWidth - startX;
            int sourceHeight = maxHeight - startY;

            if (sourceWidth <= 0 || sourceHeight <= 0)
            {
                return;
            }

            // Copy row by row due to the source offset
            for (int row = 0; row < sourceHeight; row++)
            {
                int sourceIndex = (startY + row) * width + startX;
                driver.CopyBuffer(data.AsMemory(sourceIndex, sourceWidth), x + startX, y + startY + row, sourceWidth, 1);
            }
        }
        else
        {
            driver.CopyBuffer(data.AsMemory(), x, y, width, height);
        }
    }
}
