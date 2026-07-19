using System;

namespace DevKernel.Shell;

/// <summary>
/// Colored console output shared by every shell command, so the palette and the
/// label columns stay consistent across listings instead of being re-chosen at
/// each call site.
/// </summary>
internal static class Terminal
{
    /// <summary>Column width (chars) used to pad command names and info labels in shell output.</summary>
    public const int LabelColumnWidth = 14;

    /// <summary>Indent placed before every label/value line.</summary>
    private const string LineIndent = "  ";

    /// <summary>Separator drawn between the prompt name and the working directory.</summary>
    private const string PromptSeparator = ":";

    /// <summary>Trailing sigil of the shell prompt.</summary>
    private const string PromptSigil = "$ ";

    /// <summary>Writes a section title, e.g. <c>Memory Information:</c>.</summary>
    public static void Header(string title)
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine(title);
        Console.ResetColor();
    }

    /// <summary>Writes a plain, uncolored message.</summary>
    public static void Info(string message)
    {
        Console.WriteLine(message);
    }

    /// <summary>Writes a supporting hint in gray, subordinate to the line above it.</summary>
    public static void Hint(string message)
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    /// <summary>Writes a failure message in red.</summary>
    public static void Error(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    /// <summary>Writes a success message in green.</summary>
    public static void Success(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    /// <summary>Writes a cautionary message in yellow.</summary>
    public static void Warning(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    /// <summary>Writes a de-emphasized message in dark gray.</summary>
    public static void Muted(string message)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    /// <summary>Writes an indented <c>label   value</c> line with the default label column.</summary>
    public static void InfoLine(string label, string value)
    {
        InfoLine(label, value, LabelColumnWidth);
    }

    /// <summary>Writes an indented <c>label   value</c> line, padding the label to <paramref name="labelWidth"/>.</summary>
    public static void InfoLine(string label, string value, int labelWidth)
    {
        Console.Write(LineIndent);
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write(label.PadRight(labelWidth));
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(value);
        Console.ResetColor();
    }

    /// <summary>Writes an indented label/value line whose value carries a status color (UP/DOWN, YES/NO...).</summary>
    public static void StatusLine(string label, string value, ConsoleColor valueColor)
    {
        StatusLine(label, value, valueColor, LabelColumnWidth);
    }

    /// <summary>Writes an indented label/value line whose value carries a status color, with an explicit label column.</summary>
    public static void StatusLine(string label, string value, ConsoleColor valueColor, int labelWidth)
    {
        Console.Write(LineIndent);
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write(label.PadRight(labelWidth));
        Console.ForegroundColor = valueColor;
        Console.WriteLine(value);
        Console.ResetColor();
    }

    /// <summary>Draws the <c>name:cwd$</c> prompt the shell reads a line after.</summary>
    public static void WritePrompt(string prompt, string cwd)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write(prompt);
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write(PromptSeparator);
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.Write(cwd);
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write(PromptSigil);
        Console.ResetColor();
    }
}
