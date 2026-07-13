using System;

namespace DevKernel.Shell;

/// <summary>
/// One shell command: how it is typed, what it does, and the handler that runs
/// it. <see cref="Usage"/> is the single source of truth for both the help
/// listing and the error printed when the argument count is out of range, so
/// the two can no longer drift apart.
/// </summary>
internal sealed class ShellCommand
{
    /// <summary>Upper bound for <see cref="MaxArgs"/> when a command accepts any number of trailing arguments.</summary>
    public const int UnlimitedArgs = int.MaxValue;

    /// <summary>Primary token the command is invoked by, lowercase.</summary>
    public required string Name { get; init; }

    /// <summary>Full invocation syntax, e.g. <c>mkpart &lt;disk&gt; [start_lba] &lt;size_mb&gt;</c>.</summary>
    public required string Usage { get; init; }

    /// <summary>One-line description shown by <c>help</c>.</summary>
    public required string Description { get; init; }

    /// <summary>Handler invoked once the argument count has been validated.</summary>
    public required Action<ShellContext, CommandArgs> Execute { get; init; }

    /// <summary>Additional tokens that invoke the same command (e.g. <c>cls</c> for <c>clear</c>).</summary>
    public string[] Aliases { get; init; } = Array.Empty<string>();

    /// <summary>Fewest arguments (excluding the command token) the handler accepts.</summary>
    public int MinArgs { get; init; }

    /// <summary>Most arguments (excluding the command token) the handler accepts.</summary>
    public int MaxArgs { get; init; }

    /// <summary>Help section this command is listed under; assigned by <see cref="CommandShell.Register"/>.</summary>
    public string Category { get; internal set; } = string.Empty;

    /// <summary>True when <paramref name="token"/> is this command's name or one of its aliases.</summary>
    public bool Matches(string token)
    {
        if (string.Equals(Name, token, StringComparison.Ordinal))
        {
            return true;
        }

        for (int i = 0; i < Aliases.Length; i++)
        {
            if (string.Equals(Aliases[i], token, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
