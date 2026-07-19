using Cosmos.Kernel.System.Graphics.Fonts;

namespace DevKernel.Graphics;

/// <summary>
/// Text metrics shared by the full-screen overlays, so their rows line up.
/// </summary>
internal static class OverlayLayout
{
    /// <summary>Left/top margin (pixels) of text drawn on graphics overlays.</summary>
    public const int TextMarginPx = 10;

    /// <summary>Vertical spacing (pixels) added below the font height for each text row.</summary>
    public const int LineSpacingPx = 2;

    /// <summary>Text rows advanced after the last line of a section (the line itself plus one blank separator row).</summary>
    public const int SectionBreakRowCount = 2;

    /// <summary>Vertical distance (pixels) between the baselines of two consecutive rows.</summary>
    public static int LineHeight(PCScreenFont font)
    {
        return font.Height + LineSpacingPx;
    }
}
