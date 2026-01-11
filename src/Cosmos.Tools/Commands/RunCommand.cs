using System.ComponentModel;
using System.Diagnostics;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Cosmos.Tools.Commands;

public class RunSettings : CommandSettings
{
    [CommandOption("-p|--project")]
    [Description("Path to the kernel project (default: current directory)")]
    public string? Project { get; set; }

    [CommandOption("-a|--arch")]
    [Description("Target architecture (x64, arm64)")]
    public string? Arch { get; set; }

    [CommandOption("-i|--iso")]
    [Description("Direct path to ISO file (overrides project detection)")]
    public string? Iso { get; set; }

    [CommandOption("-d|--debug")]
    [Description("Start QEMU with GDB server for debugging (-s -S)")]
    public bool Debug { get; set; }

    [CommandOption("-m|--memory")]
    [Description("Amount of memory for the VM")]
    [DefaultValue("512M")]
    public string Memory { get; set; } = "512M";

    [CommandOption("-n|--no-graphics")]
    [Description("Run without graphical display (serial only)")]
    public bool NoGraphics { get; set; }

    [CommandOption("-b|--build")]
    [Description("Build before running")]
    public bool Build { get; set; }
}

public class RunCommand : AsyncCommand<RunSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, RunSettings settings)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [bold]Cosmos Kernel Runner[/]");
        AnsiConsole.WriteLine("  " + new string('-', 50));

        var isoPath = settings.Iso;
        var arch = settings.Arch;

        if (string.IsNullOrEmpty(isoPath))
        {
            (isoPath, arch) = FindIsoFile(settings.Project, arch);
        }

        if (string.IsNullOrEmpty(isoPath) || !File.Exists(isoPath))
        {
            AnsiConsole.MarkupLine("  [red]No ISO file found.[/]");
            AnsiConsole.WriteLine();

            if (settings.Build)
            {
                AnsiConsole.WriteLine("  Building project first...");
            }
            else
            {
                AnsiConsole.MarkupLine("  Run [blue]cosmos build[/] first, or use [blue]--build[/] flag.");
            }
            return 1;
        }

        arch ??= DetectArchFromPath(isoPath);

        AnsiConsole.MarkupLine($"  ISO: [blue]{isoPath}[/]");
        AnsiConsole.MarkupLine($"  Architecture: [blue]{arch}[/]");
        AnsiConsole.MarkupLine($"  Memory: [blue]{settings.Memory}[/]");
        AnsiConsole.MarkupLine($"  Debug: [blue]{(settings.Debug ? "Yes (GDB on :1234)" : "No")}[/]");
        AnsiConsole.WriteLine("  " + new string('-', 50));
        AnsiConsole.WriteLine();

        var (qemuCommand, qemuArgs) = BuildQemuCommand(isoPath, arch, settings.Memory, settings.Debug, settings.NoGraphics);

        AnsiConsole.MarkupLine($"  [dim]$ {qemuCommand} {qemuArgs}[/]");
        AnsiConsole.WriteLine();

        if (settings.Debug)
        {
            AnsiConsole.MarkupLine("  [cyan]QEMU is waiting for debugger connection.[/]");
            AnsiConsole.MarkupLine("  [cyan]Connect with: gdb -ex 'target remote localhost:1234'[/]");
            AnsiConsole.WriteLine();
        }

        AnsiConsole.WriteLine("  Press Ctrl+C to stop QEMU");
        AnsiConsole.WriteLine();

        await RunQemuAsync(qemuCommand, qemuArgs);
        return 0;
    }

    private static (string? isoPath, string? arch) FindIsoFile(string? projectPath, string? arch)
    {
        var searchDir = projectPath ?? Directory.GetCurrentDirectory();

        if (File.Exists(searchDir) && searchDir.EndsWith(".csproj"))
        {
            searchDir = Path.GetDirectoryName(searchDir)!;
        }

        if (!string.IsNullOrEmpty(arch))
        {
            var outputDir = Path.Combine(searchDir, $"output-{arch}");
            if (Directory.Exists(outputDir))
            {
                var isoFiles = Directory.GetFiles(outputDir, "*.iso");
                if (isoFiles.Length > 0)
                {
                    return (isoFiles[0], arch);
                }
            }
        }

        foreach (var archName in new[] { "x64", "arm64" })
        {
            var outputDir = Path.Combine(searchDir, $"output-{archName}");
            if (Directory.Exists(outputDir))
            {
                var isoFiles = Directory.GetFiles(outputDir, "*.iso");
                if (isoFiles.Length > 0)
                {
                    return (isoFiles[0], archName);
                }
            }
        }

        var binDir = Path.Combine(searchDir, "bin");
        if (Directory.Exists(binDir))
        {
            foreach (var config in new[] { "Debug", "Release" })
            {
                foreach (var archName in new[] { "x64", "arm64" })
                {
                    var rid = archName == "arm64" ? "linux-arm64" : "linux-x64";
                    var publishDir = Path.Combine(binDir, config, "net10.0", rid, "publish");
                    if (Directory.Exists(publishDir))
                    {
                        var isoFiles = Directory.GetFiles(publishDir, "*.iso");
                        if (isoFiles.Length > 0)
                        {
                            return (isoFiles[0], archName);
                        }
                    }
                }
            }
        }

        var cosmosDir = Path.Combine(searchDir, "cosmos");
        if (Directory.Exists(cosmosDir))
        {
            var isoFiles = Directory.GetFiles(cosmosDir, "*.iso");
            if (isoFiles.Length > 0)
            {
                return (isoFiles[0], arch ?? "x64");
            }
        }

        return (null, null);
    }

    private static string DetectArchFromPath(string isoPath)
    {
        var pathLower = isoPath.ToLower();
        if (pathLower.Contains("arm64") || pathLower.Contains("aarch64"))
            return "arm64";
        return "x64";
    }

    private static (string command, string args) BuildQemuCommand(string isoPath, string? arch, string memory, bool debug, bool noGraphics)
    {
        var args = new List<string>();

        if (arch == "arm64")
        {
            args.AddRange([
                "-M", "virt",
                "-cpu", "cortex-a72",
                "-m", memory
            ]);

            var uefiPaths = new[]
            {
                "/usr/share/AAVMF/AAVMF_CODE.fd",
                "/usr/share/qemu-efi-aarch64/QEMU_EFI.fd",
                "/usr/share/edk2/aarch64/QEMU_EFI.fd",
                "/opt/homebrew/share/qemu/edk2-aarch64-code.fd"
            };

            var uefiPath = uefiPaths.FirstOrDefault(File.Exists);
            if (uefiPath != null)
            {
                args.AddRange(["-bios", uefiPath]);
            }

            args.AddRange([
                "-drive", $"if=none,id=cd,file={isoPath}",
                "-device", "virtio-scsi-pci",
                "-device", "scsi-cd,drive=cd,bootindex=0"
            ]);

            if (!noGraphics)
            {
                args.AddRange([
                    "-device", "virtio-keyboard-device",
                    "-device", "ramfb",
                    "-display", "gtk,show-cursor=on"
                ]);
            }
            else
            {
                args.AddRange(["-nographic"]);
            }

            args.AddRange(["-serial", "stdio"]);

            if (debug)
            {
                args.AddRange(["-s", "-S"]);
            }

            return ("qemu-system-aarch64", string.Join(" ", args));
        }
        else
        {
            args.AddRange([
                "-M", "q35",
                "-cpu", "max",
                "-m", memory,
                "-cdrom", isoPath
            ]);

            if (!noGraphics)
            {
                args.AddRange([
                    "-display", "gtk",
                    "-vga", "std"
                ]);
            }
            else
            {
                args.AddRange(["-nographic"]);
            }

            args.AddRange([
                "-serial", "stdio",
                "-no-reboot",
                "-no-shutdown"
            ]);

            if (debug)
            {
                args.AddRange(["-s", "-S"]);
            }

            return ("qemu-system-x86_64", string.Join(" ", args));
        }
    }

    private static async Task RunQemuAsync(string command, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
                UseShellExecute = false
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                AnsiConsole.MarkupLine($"  [red]Failed to start {command}[/]");
                AnsiConsole.MarkupLine("  Make sure QEMU is installed: [blue]cosmos check[/]");
                return;
            }

            await process.WaitForExitAsync();

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"  QEMU exited with code: [blue]{process.ExitCode}[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"  [red]Error running QEMU: {Markup.Escape(ex.Message)}[/]");
            AnsiConsole.MarkupLine("  Make sure QEMU is installed: [blue]cosmos install[/]");
        }
    }
}
