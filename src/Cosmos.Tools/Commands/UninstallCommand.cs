using System.ComponentModel;
using System.Diagnostics;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Cosmos.Tools.Commands;

public class UninstallSettings : CommandSettings
{
    [CommandOption("--keep-extension")]
    [Description("Keep VS Code extension installed")]
    public bool KeepExtension { get; set; }

    [CommandOption("-y|--auto")]
    [Description("Automatically uninstall without prompting")]
    public bool Auto { get; set; }
}

public class UninstallCommand : AsyncCommand<UninstallSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, UninstallSettings settings)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [bold]Cosmos Tools Uninstaller[/]");
        AnsiConsole.WriteLine("  " + new string('-', 50));
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("  This will uninstall:");
        AnsiConsole.MarkupLine("    - Cosmos.Patcher (dotnet tool)");
        AnsiConsole.MarkupLine("    - Cosmos.Build.Templates (dotnet templates)");
        if (!settings.KeepExtension)
        {
            AnsiConsole.MarkupLine("    - Cosmos VS Code extension");
        }
        AnsiConsole.WriteLine();

        if (!settings.Auto)
        {
            var proceed = AnsiConsole.Confirm("  Proceed with uninstallation?", false);
            if (!proceed)
            {
                AnsiConsole.WriteLine("  Uninstallation cancelled.");
                return 0;
            }
        }

        AnsiConsole.WriteLine();

        // Uninstall Cosmos.Patcher tool
        AnsiConsole.Markup("  Uninstalling Cosmos.Patcher... ");
        await UninstallDotnetToolAsync("Cosmos.Patcher");

        // Uninstall Cosmos templates
        AnsiConsole.Markup("  Uninstalling Cosmos.Build.Templates... ");
        await UninstallTemplateAsync("Cosmos.Build.Templates");

        // Uninstall VS Code extension
        if (!settings.KeepExtension)
        {
            await UninstallVSCodeExtensionAsync();
        }

        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("  " + new string('-', 50));
        AnsiConsole.MarkupLine("  [green]Uninstallation complete![/]");
        AnsiConsole.WriteLine();

        return 0;
    }

    private static async Task UninstallDotnetToolAsync(string packageName)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"tool uninstall -g {packageName}",
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
            AnsiConsole.MarkupLine(process.ExitCode == 0 ? "[green]OK[/]" : "[dim]not installed[/]");
        }
        catch
        {
            AnsiConsole.MarkupLine("[yellow]SKIPPED[/]");
        }
    }

    private static async Task UninstallTemplateAsync(string packageName)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"new uninstall {packageName}",
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
            AnsiConsole.MarkupLine(process.ExitCode == 0 ? "[green]OK[/]" : "[dim]not installed[/]");
        }
        catch
        {
            AnsiConsole.MarkupLine("[yellow]SKIPPED[/]");
        }
    }

    private static async Task UninstallVSCodeExtensionAsync()
    {
        var codeCommand = GetVSCodeCommand();
        if (codeCommand == null)
        {
            AnsiConsole.MarkupLine("  [dim]VS Code not found, skipping extension uninstall[/]");
            return;
        }

        AnsiConsole.Markup("  Uninstalling VS Code extension... ");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = codeCommand,
                Arguments = "--uninstall-extension cosmosos.cosmos-vscode",
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
            AnsiConsole.MarkupLine(process.ExitCode == 0 ? "[green]OK[/]" : "[dim]not installed[/]");
        }
        catch
        {
            AnsiConsole.MarkupLine("[yellow]SKIPPED[/]");
        }
    }

    private static string? GetVSCodeCommand()
    {
        var commands = new[] { "code", "code-insiders", "codium" };

        foreach (var cmd in commands)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = cmd,
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    process.WaitForExit(3000);
                    if (process.ExitCode == 0)
                    {
                        return cmd;
                    }
                }
            }
            catch { }
        }

        return null;
    }
}
