using System.ComponentModel;
using System.Diagnostics;
using Cosmos.Tools.Launcher;
using Cosmos.Tools.Platform;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Cosmos.Tools.Commands;

public class RunSettings : CommandSettings
{
    [CommandOption("-p|--project")]
    [Description("Path to the kernel project (default: current directory). Used to locate the ISO when --iso is not given.")]
    public string? Project { get; set; }

    [CommandOption("-a|--arch")]
    [Description("Target architecture (x64, arm64). Default: x64.")]
    [DefaultValue("x64")]
    public string Arch { get; set; } = "x64";

    [CommandOption("--iso")]
    [Description("Explicit path to the ISO to boot. If omitted, looks in output-<arch>/ for a *.iso.")]
    public string? IsoPath { get; set; }

    [CommandOption("-m|--memory")]
    [Description("Memory in MB. Default: 512.")]
    [DefaultValue(512)]
    public int MemoryMb { get; set; } = 512;

    [CommandOption("--headless")]
    [Description("Run without a display window (serial-only).")]
    public bool Headless { get; set; }

    [CommandOption("--debug")]
    [Description("Wait for a GDB connection on port 1234 (-s -S).")]
    public bool Debug { get; set; }

    [CommandOption("--disk <SPEC>")]
    [Description("Attach a disk image the kernel can use at boot. Format: 'path' or 'path,kind' where kind is ahci (default) or nvme. Repeatable.")]
    public string[] Disks { get; set; } = Array.Empty<string>();

    [CommandOption("--nic <MODEL>")]
    [Description("Network card exposed to the guest: 'none' for no card, or a QEMU model like e1000e or virtio-net-device. Omit to keep QEMU's default NIC.")]
    public string? Nic { get; set; }

    [CommandOption("--keyboard <MODEL>")]
    [Description("Keyboard device to attach, e.g. virtio-keyboard-device (needed on arm64 virt). 'ps2'/'none' add nothing; x64 PS/2 is built into the chipset.")]
    public string? Keyboard { get; set; }

    [CommandOption("--mouse <MODEL>")]
    [Description("Mouse device to attach, e.g. virtio-mouse-device (needed on arm64 virt). 'ps2'/'none' add nothing; x64 PS/2 is built into the chipset.")]
    public string? Mouse { get; set; }
}

public class RunCommand : AsyncCommand<RunSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, RunSettings settings)
    {
        if (settings.Arch != "x64" && settings.Arch != "arm64")
        {
            AnsiConsole.MarkupLine($"  [red]Unsupported architecture: {settings.Arch}[/]");
            return 1;
        }

        string? isoPath = settings.IsoPath ?? FindIso(settings.Project, settings.Arch);
        if (isoPath is null || !File.Exists(isoPath))
        {
            AnsiConsole.MarkupLine($"  [red]No ISO found.[/] Build the kernel first (`cosmos build -a {settings.Arch}`) or pass --iso PATH.");
            return 1;
        }

        List<DiskAttachment> disks;
        try
        {
            disks = ParseDisks(settings.Disks);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"  [red]{Markup.Escape(ex.Message)}[/]");
            return 1;
        }

        QemuLaunchPlan plan;
        try
        {
            plan = await QemuLauncher.BuildAsync(new QemuLaunchOptions
            {
                Architecture = settings.Arch,
                IsoPath = isoPath,
                MemoryMb = settings.MemoryMb,
                Headless = settings.Headless,
                Debug = settings.Debug,
                SerialOutputFile = null, // CLI: serial → stdio
                Disks = disks,
                NetworkCard = settings.Nic,
                KeyboardDevice = settings.Keyboard,
                MouseDevice = settings.Mouse,
                ExtraArgs = context.Remaining.Raw.ToArray()
            });
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"  [red]{Markup.Escape(ex.Message)}[/]");
            return 1;
        }

        AnsiConsole.MarkupLine($"  Running [blue]{Path.GetFileName(isoPath)}[/] ([blue]{settings.Arch}[/]) via QEMU [dim]({plan.Source.ToString().ToLowerInvariant()}: {plan.BinaryPath})[/]");
        if (plan.Source == ToolSource.Bundle)
        {
            AnsiConsole.MarkupLine($"  [dim]> {Markup.Escape(plan.BinaryPath)} {Markup.Escape(plan.Arguments)}[/]");
        }

        // Inherit stdio so the user can interact with the kernel via the serial console.
        var psi = new ProcessStartInfo
        {
            FileName = plan.BinaryPath,
            Arguments = plan.Arguments,
            UseShellExecute = false
        };
        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                AnsiConsole.MarkupLine("  [red]Failed to start QEMU[/]");
                return 1;
            }
            // Make QEMU die with us. Without this, callers that kill cosmos
            // (e.g. VS Code's debug Stop button issuing TerminateProcess on
            // Windows or SIGTERM on Unix) leave QEMU running as an orphan.
            using var lifetime = ChildProcessLifetime.AttachTo(process);
            await process.WaitForExitAsync();
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"  [red]Failed to start QEMU: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }

    /// <summary>
    /// Turns <c>--disk</c> specs (<c>path[,kind]</c>) into <see cref="DiskAttachment"/>s.
    /// The kind suffix is optional and defaults to AHCI; the split is on the last
    /// comma so a bare path (the common case) is never mistaken for a kind. The
    /// image must already exist — the launcher would otherwise fail deep inside
    /// QEMU with a less obvious message.
    /// </summary>
    internal static List<DiskAttachment> ParseDisks(string[] specs)
    {
        var disks = new List<DiskAttachment>(specs.Length);
        foreach (string spec in specs)
        {
            string path = spec;
            DiskKind kind = DiskKind.Ahci;

            int comma = spec.LastIndexOf(',');
            if (comma >= 0)
            {
                string suffix = spec[(comma + 1)..].Trim();
                if (suffix.Equals("ahci", StringComparison.OrdinalIgnoreCase))
                {
                    kind = DiskKind.Ahci;
                    path = spec[..comma];
                }
                else if (suffix.Equals("nvme", StringComparison.OrdinalIgnoreCase))
                {
                    kind = DiskKind.Nvme;
                    path = spec[..comma];
                }
                // Any other suffix is treated as part of the path (a filename that
                // legitimately contains a comma), leaving the default AHCI kind.
            }

            path = path.Trim();
            if (path.Length == 0)
            {
                throw new ArgumentException($"Empty disk path in --disk spec: '{spec}'");
            }
            if (!File.Exists(path))
            {
                throw new ArgumentException($"Disk image not found: '{path}'");
            }

            disks.Add(new DiskAttachment { Path = Path.GetFullPath(path), Kind = kind });
        }
        return disks;
    }

    private static string? FindIso(string? projectPath, string arch)
    {
        string baseDir = projectPath is not null && Directory.Exists(projectPath)
            ? projectPath
            : Directory.GetCurrentDirectory();
        string outputDir = Path.Combine(baseDir, $"output-{arch}");
        if (!Directory.Exists(outputDir))
        {
            return null;
        }
        return Directory.EnumerateFiles(outputDir, "*.iso").FirstOrDefault();
    }
}
