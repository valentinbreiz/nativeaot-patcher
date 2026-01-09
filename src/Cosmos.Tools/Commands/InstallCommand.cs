using System.ComponentModel;
using System.Diagnostics;
using Spectre.Console;
using Spectre.Console.Cli;
using Cosmos.Tools.Platform;

namespace Cosmos.Tools.Commands;

public class InstallSettings : CommandSettings
{
    [CommandOption("-a|--arch")]
    [Description("Target architecture (x64, arm64, or 'all' for both)")]
    public string? Arch { get; set; }

    [CommandOption("-t|--tool")]
    [Description("Specific tool to install (e.g., 'yasm', 'lld')")]
    public string? Tool { get; set; }

    [CommandOption("-y|--auto")]
    [Description("Automatically install without prompting")]
    public bool Auto { get; set; }
}

public class InstallCommand : AsyncCommand<InstallSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, InstallSettings settings)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [bold]Cosmos Tools Installer[/]");
        AnsiConsole.WriteLine("  " + new string('-', 50));
        AnsiConsole.MarkupLine($"  Platform: [blue]{PlatformInfo.GetDistroName()}[/] ({PlatformInfo.CurrentArch})");
        AnsiConsole.MarkupLine($"  Package Manager: [blue]{PlatformInfo.GetPackageManager()}[/]");
        AnsiConsole.WriteLine("  " + new string('-', 50));
        AnsiConsole.WriteLine();

        var results = await ToolChecker.CheckAllToolsAsync(settings.Arch);
        var missingTools = results.Where(r => !r.Found).ToList();

        if (!string.IsNullOrEmpty(settings.Tool))
        {
            missingTools = missingTools
                .Where(r => r.Tool.Name.Contains(settings.Tool, StringComparison.OrdinalIgnoreCase) ||
                           r.Tool.DisplayName.Contains(settings.Tool, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (missingTools.Count == 0)
        {
            AnsiConsole.MarkupLine("  [green]All system tools are already installed![/]");
        }
        else
        {
            AnsiConsole.WriteLine("  System tools to install:");
            AnsiConsole.WriteLine();

            var installPlan = new List<(ToolStatus status, InstallInfo? info, string action)>();

            foreach (var missing in missingTools)
            {
                var info = missing.Tool.GetInstallInfo(PlatformInfo.CurrentOS);
                var action = GetInstallAction(info);
                installPlan.Add((missing, info, action));

                var required = missing.Tool.Required ? "" : " [dim](optional)[/]";
                AnsiConsole.MarkupLine($"  - [white]{missing.Tool.DisplayName}[/]{required}");
                AnsiConsole.MarkupLine($"    [cyan]{action}[/]");
            }

            AnsiConsole.WriteLine();

            if (!settings.Auto)
            {
                var proceed = AnsiConsole.Confirm("  Proceed with installation?", true);
                if (!proceed)
                {
                    AnsiConsole.WriteLine("  Installation cancelled.");
                    return 0;
                }
            }

            AnsiConsole.WriteLine();

            var packageManager = PlatformInfo.GetPackageManager();
            var packagesToInstall = new List<string>();

            foreach (var (status, info, action) in installPlan)
            {
                if (info == null)
                {
                    PrintManualInstruction(status.Tool);
                    continue;
                }

                switch (info.Method)
                {
                    case "package":
                        var packages = GetPackagesForManager(info, packageManager);
                        if (packages != null)
                        {
                            packagesToInstall.AddRange(packages);
                        }
                        else
                        {
                            PrintManualInstruction(status.Tool, info);
                        }
                        break;

                    case "download":
                        await HandleDownloadInstall(status.Tool, info);
                        break;

                    case "manual":
                    default:
                        PrintManualInstruction(status.Tool, info);
                        break;
                }
            }

            if (packagesToInstall.Count > 0)
            {
                await InstallPackagesAsync(packageManager, packagesToInstall);
            }
        }

        // Install/update Cosmos dotnet tools and templates
        await InstallDotnetToolsAsync();

        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("  " + new string('-', 50));
        AnsiConsole.MarkupLine("  [green]Installation complete![/]");
        AnsiConsole.MarkupLine("  Run [blue]cosmos-tools check[/] to verify installation.");
        AnsiConsole.WriteLine();

        return 0;
    }

    private static async Task InstallDotnetToolsAsync()
    {
        AnsiConsole.WriteLine();

        // Install Cosmos.Patcher tool
        AnsiConsole.Markup("  Installing Cosmos.Patcher... ");
        await InstallDotnetToolAsync("Cosmos.Patcher");

        // Install Cosmos templates
        AnsiConsole.Markup("  Installing Cosmos.Build.Templates... ");
        await InstallTemplateAsync("Cosmos.Build.Templates");
    }

    private static async Task InstallDotnetToolAsync(string packageName)
    {
        try
        {
            // Try to update first (works if already installed)
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"tool update -g {packageName}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                AnsiConsole.MarkupLine("[yellow]SKIPPED[/]");
                return;
            }

            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                AnsiConsole.MarkupLine("[green]OK[/]");
            }
            else
            {
                // Try install if update failed
                psi.Arguments = $"tool install -g {packageName}";
                using var installProcess = Process.Start(psi);
                if (installProcess != null)
                {
                    await installProcess.WaitForExitAsync();
                    AnsiConsole.MarkupLine(installProcess.ExitCode == 0 ? "[green]OK[/]" : "[yellow]SKIPPED[/]");
                }
            }
        }
        catch
        {
            AnsiConsole.MarkupLine("[yellow]SKIPPED[/]");
        }
    }

    private static async Task InstallTemplateAsync(string packageName)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"new install {packageName}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                AnsiConsole.MarkupLine("[yellow]SKIPPED[/]");
                return;
            }

            await process.WaitForExitAsync();
            AnsiConsole.MarkupLine(process.ExitCode == 0 ? "[green]OK[/]" : "[yellow]SKIPPED[/]");
        }
        catch
        {
            AnsiConsole.MarkupLine("[yellow]SKIPPED[/]");
        }
    }

    private static string GetInstallAction(InstallInfo? info)
    {
        if (info == null)
            return "Manual installation required";

        var packageManager = PlatformInfo.GetPackageManager();
        var packages = GetPackagesForManager(info, packageManager);

        return info.Method switch
        {
            "package" when packages != null => $"{packageManager} install {string.Join(" ", packages)}",
            "download" => $"Download from {info.DownloadUrl}",
            "build" => "Build from source",
            "manual" => info.ManualInstructions ?? "Manual installation required",
            _ => "Manual installation required"
        };
    }

    private static string[]? GetPackagesForManager(InstallInfo info, string packageManager)
    {
        return packageManager switch
        {
            "apt" => info.AptPackages,
            "dnf" => info.DnfPackages,
            "pacman" => info.PacmanPackages,
            "brew" => info.BrewPackages,
            "choco" => info.ChocoPackages,
            _ => null
        };
    }

    private static async Task InstallPackagesAsync(string packageManager, List<string> packages)
    {
        if (packages.Count == 0)
            return;

        AnsiConsole.MarkupLine($"  Installing packages via [blue]{packageManager}[/]...");
        AnsiConsole.WriteLine();

        var (command, args) = packageManager switch
        {
            "apt" => ("sudo", $"apt-get install -y {string.Join(" ", packages)}"),
            "dnf" => ("sudo", $"dnf install -y {string.Join(" ", packages)}"),
            "pacman" => ("sudo", $"pacman -S --noconfirm {string.Join(" ", packages)}"),
            "brew" => ("brew", $"install {string.Join(" ", packages)}"),
            "choco" => ("choco", $"install -y {string.Join(" ", packages)}"),
            _ => throw new InvalidOperationException($"Unknown package manager: {packageManager}")
        };

        AnsiConsole.MarkupLine($"  [dim]$ {command} {args}[/]");
        AnsiConsole.WriteLine();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                AnsiConsole.MarkupLine("  [red]Failed to start package manager[/]");
                return;
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (!string.IsNullOrWhiteSpace(output))
            {
                foreach (var line in output.Split('\n').Take(20))
                {
                    AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(line)}[/]");
                }
            }

            if (process.ExitCode == 0)
            {
                AnsiConsole.MarkupLine("  [green]Packages installed successfully[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"  [red]Package installation failed (exit code: {process.ExitCode})[/]");
                if (!string.IsNullOrWhiteSpace(error))
                {
                    AnsiConsole.MarkupLine($"  [red]{Markup.Escape(error)}[/]");
                }
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"  [red]Error: {Markup.Escape(ex.Message)}[/]");
        }
    }

    private static async Task HandleDownloadInstall(ToolDefinition tool, InstallInfo info)
    {
        AnsiConsole.MarkupLine($"  Downloading [white]{tool.DisplayName}[/]...");

        if (string.IsNullOrEmpty(info.DownloadUrl))
        {
            PrintManualInstruction(tool, info);
            return;
        }

        var toolsPath = ToolChecker.GetCosmosToolsPath();
        Directory.CreateDirectory(toolsPath);

        AnsiConsole.MarkupLine($"  [cyan]Download from: {info.DownloadUrl}[/]");
        AnsiConsole.MarkupLine($"  [cyan]Extract to: {toolsPath}[/]");
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("  [yellow]Automatic download not yet implemented.[/]");
        AnsiConsole.MarkupLine("  [yellow]Please download and extract manually:[/]");
        AnsiConsole.MarkupLine($"    1. Download from: {info.DownloadUrl}");
        AnsiConsole.MarkupLine($"    2. Extract to: {toolsPath}");
        AnsiConsole.MarkupLine($"    3. Add to PATH: export PATH=\"{toolsPath}/bin:$PATH\"");

        await Task.CompletedTask;
    }

    private static void PrintManualInstruction(ToolDefinition tool, InstallInfo? info = null)
    {
        AnsiConsole.MarkupLine($"  [yellow]Manual installation required for {tool.DisplayName}:[/]");

        if (info?.ManualInstructions != null)
        {
            AnsiConsole.MarkupLine($"    {info.ManualInstructions}");
        }
        else if (info?.DownloadUrl != null)
        {
            AnsiConsole.MarkupLine($"    Download from: {info.DownloadUrl}");
        }
        else
        {
            AnsiConsole.MarkupLine($"    Please install {tool.Name} manually.");
        }

        AnsiConsole.WriteLine();
    }
}
