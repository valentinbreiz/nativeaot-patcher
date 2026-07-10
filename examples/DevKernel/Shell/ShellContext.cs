using DevKernel.Network;

namespace DevKernel.Shell;

/// <summary>
/// The mutable state one shell session carries between commands: where the user
/// is in the VFS, and how the primary NIC was configured.
/// </summary>
internal sealed class ShellContext
{
    /// <summary>Name shown before the colon in the prompt.</summary>
    private const string DefaultPrompt = "cosmos";

    public ShellContext(CommandShell shell)
    {
        Shell = shell;
    }

    /// <summary>The registry backing this session, so <c>help</c> can enumerate it.</summary>
    public CommandShell Shell { get; }

    /// <summary>Name shown before the colon in the prompt.</summary>
    public string Prompt { get; set; } = DefaultPrompt;

    /// <summary>Current working directory: shown in the prompt and used to resolve relative paths.</summary>
    public string Cwd { get; set; } = VfsPath.Root;

    /// <summary>IPv4 configuration applied to the primary NIC by <c>netconfig</c> or <c>dhcp</c>.</summary>
    public NetworkSession Network { get; } = new();

    /// <summary>Makes <paramref name="path"/> absolute against <see cref="Cwd"/>, without collapsing <c>..</c>.</summary>
    public string Resolve(string path)
    {
        return VfsPath.Resolve(Cwd, path);
    }

    /// <summary>Makes <paramref name="path"/> absolute against <see cref="Cwd"/> and collapses <c>.</c> / <c>..</c>.</summary>
    public string ResolveNormalized(string path)
    {
        return VfsPath.Normalize(VfsPath.Resolve(Cwd, path));
    }
}
