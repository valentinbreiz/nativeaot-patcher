using System;
using System.Globalization;

namespace DevKernel.Shell;

/// <summary>
/// The arguments of one command invocation. Indexing is zero-based over the
/// arguments alone: the command token itself is not an argument.
/// </summary>
internal readonly struct CommandArgs
{
    /// <summary>Separator between the command token and its arguments.</summary>
    private const char TokenSeparator = ' ';

    /// <summary>Tokens of the input line, <c>[0]</c> being the command as typed.</summary>
    private readonly string[] _tokens;

    public CommandArgs(ShellCommand command, string line, string[] tokens)
    {
        Command = command;
        _tokens = tokens;

        // Everything after the first separator, verbatim: `echo` must preserve
        // the spacing the user typed, which the token split has already lost.
        int split = line.IndexOf(TokenSeparator);
        RawTail = split < 0 ? string.Empty : line.Substring(split + 1);
    }

    /// <summary>The command these arguments were parsed for, so a handler can report its own usage.</summary>
    public ShellCommand Command { get; }

    /// <summary>Number of arguments supplied after the command token.</summary>
    public int Count => _tokens.Length - 1;

    /// <summary>The command token as the user typed it (before lowercasing).</summary>
    public string CommandToken => _tokens[0];

    /// <summary>Everything typed after the command token, with the original spacing.</summary>
    public string RawTail { get; }

    /// <summary>The argument at <paramref name="index"/>, zero-based.</summary>
    public string this[int index] => _tokens[index + 1];

    /// <summary>The argument at <paramref name="index"/>, or <paramref name="fallback"/> when absent.</summary>
    public string GetOrDefault(int index, string fallback)
    {
        return index < Count ? this[index] : fallback;
    }

    /// <summary>Joins the arguments from <paramref name="startIndex"/> onward with single spaces.</summary>
    public string Join(int startIndex)
    {
        if (startIndex >= Count)
        {
            return string.Empty;
        }

        string result = this[startIndex];
        for (int i = startIndex + 1; i < Count; i++)
        {
            result = result + TokenSeparator + this[i];
        }

        return result;
    }

    /// <summary>Parses the argument at <paramref name="index"/> as a signed decimal integer.</summary>
    public bool TryGetInt(int index, out int value)
    {
        value = 0;
        return index < Count && int.TryParse(this[index], out value);
    }

    /// <summary>Parses the argument at <paramref name="index"/> as an unsigned decimal integer.</summary>
    public bool TryGetUInt(int index, out uint value)
    {
        value = 0;
        return index < Count && uint.TryParse(this[index], out value);
    }

    /// <summary>Parses the argument at <paramref name="index"/> as an unsigned decimal 64-bit integer.</summary>
    public bool TryGetULong(int index, out ulong value)
    {
        value = 0;
        return index < Count && ulong.TryParse(this[index], out value);
    }

    /// <summary>Parses the argument at <paramref name="index"/> as a two-digit hexadecimal byte (e.g. <c>A5</c>).</summary>
    public bool TryGetHexByte(int index, out byte value)
    {
        value = 0;
        return index < Count && byte.TryParse(this[index], NumberStyles.HexNumber, null, out value);
    }

    /// <summary>The argument at <paramref name="index"/>, lowercased for case-insensitive matching.</summary>
    public string GetLower(int index)
    {
        return this[index].ToLower();
    }

    /// <summary>Reports how the command should have been invoked; for handlers whose own parsing failed.</summary>
    public void PrintUsage()
    {
        CommandShell.PrintUsage(Command);
    }
}
