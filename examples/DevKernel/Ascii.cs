namespace DevKernel;

/// <summary>
/// ASCII classification applied whenever raw bytes (file contents, UDP
/// payloads) are echoed to the console, so control bytes cannot scramble it.
/// </summary>
internal static class Ascii
{
    /// <summary>First printable ASCII code (space); lower bound of the printable filter.</summary>
    public const int PrintableMin = 32;

    /// <summary>ASCII DEL code; exclusive upper bound of the printable filter.</summary>
    public const int PrintableLimit = 127;

    /// <summary>True when <paramref name="c"/> renders as a visible glyph on the console.</summary>
    public static bool IsPrintable(char c)
    {
        return c >= PrintableMin && c < PrintableLimit;
    }
}
