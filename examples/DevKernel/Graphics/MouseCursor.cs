using System.Drawing;
using Cosmos.Kernel.System.Graphics;

namespace DevKernel.Graphics;

/// <summary>
/// A simple arrow mouse cursor, blitted pixel by pixel onto a canvas.
/// </summary>
internal static class MouseCursor
{
    /// <summary>Width (pixels) of the cursor bitmap, and the stride of <see cref="s_pattern"/>.</summary>
    private const int CursorWidth = 10;

    /// <summary>Height (pixels) of the cursor bitmap.</summary>
    private const int CursorHeight = 16;

    /// <summary>Pattern code for a border (black) pixel.</summary>
    private const int PatternBorder = 1;

    /// <summary>Pattern code for a fill (white) pixel.</summary>
    private const int PatternFill = 2;

    /// <summary>Row-major arrow bitmap; allocated once and reused every frame.</summary>
    private static readonly int[] s_pattern = new int[]
    {
        1, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        1, 1, 0, 0, 0, 0, 0, 0, 0, 0,
        1, 2, 1, 0, 0, 0, 0, 0, 0, 0,
        1, 2, 2, 1, 0, 0, 0, 0, 0, 0,
        1, 2, 2, 2, 1, 0, 0, 0, 0, 0,
        1, 2, 2, 2, 2, 1, 0, 0, 0, 0,
        1, 2, 2, 2, 2, 2, 1, 0, 0, 0,
        1, 2, 2, 2, 2, 2, 2, 1, 0, 0,
        1, 2, 2, 2, 2, 2, 2, 2, 1, 0,
        1, 2, 2, 2, 2, 2, 2, 2, 2, 1,
        1, 2, 2, 2, 2, 2, 1, 1, 1, 1,
        1, 2, 2, 1, 2, 2, 1, 0, 0, 0,
        1, 2, 1, 0, 1, 2, 2, 1, 0, 0,
        1, 1, 0, 0, 1, 2, 2, 1, 0, 0,
        1, 0, 0, 0, 0, 1, 2, 2, 1, 0,
        0, 0, 0, 0, 0, 1, 1, 1, 1, 0,
    };

    /// <summary>Draws the cursor with its hotspot at (<paramref name="x"/>, <paramref name="y"/>), clipped to the canvas.</summary>
    public static void Draw(Canvas canvas, int x, int y)
    {
        for (int cy = 0; cy < CursorHeight; cy++)
        {
            for (int cx = 0; cx < CursorWidth; cx++)
            {
                int px = x + cx;
                int py = y + cy;

                if (px < 0 || px >= canvas.Mode.Width || py < 0 || py >= canvas.Mode.Height)
                {
                    continue;
                }

                int pixel = s_pattern[cy * CursorWidth + cx];
                if (pixel == PatternBorder)
                {
                    canvas.DrawPoint(Color.Black, px, py);
                }
                else if (pixel == PatternFill)
                {
                    canvas.DrawPoint(Color.White, px, py);
                }

                // Any other code is transparent: leave the canvas as it was.
            }
        }
    }
}
