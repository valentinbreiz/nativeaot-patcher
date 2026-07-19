using System.ComponentModel;
using System.Diagnostics;
using Cosmos.Tools.Platform;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Cosmos.Tools.Commands;

public class UninstallSettings : CommandSettings
{
    [CommandOption("-y|--auto")]
    [Description("Automatically uninstall without prompting")]
    public bool Auto { get; set; }

    [CommandOption("--tools")]
    [Description("Only remove system tools (QEMU, clang, lld, xorriso)")]
    public bool Tools { get; set; }

    [CommandOption("--packages")]
    [Description("Only remove Cosmos dotnet tools, templates, and VS Code extension")]
    public bool Packages { get; set; }
}

public class UninstallCommand : AsyncCommand<UninstallSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, UninstallSettings settings)
    {
        CommandHelper.PrintHeader("Cosmos Tools Uninstaller");

        if (!settings.Auto)
        {
            bool proceed = AnsiConsole.Confirm("  Proceed with uninstallation?", false);
            if (!proceed)
            {
                AnsiConsole.WriteLine("  Uninstallation cancelled.");
                return 0;
            }
            AnsiConsole.WriteLine();
        }

        // When neither --tools nor --packages is specified, remove everything
        bool removeTools = !settings.Packages || settings.Tools;
        bool removePackages = !settings.Tools || settings.Packages;

        // Remove system tools — delete each bundle directory under ~/.cosmos/tools/
        if (removeTools)
        {
            string toolsPath = ToolChecker.GetCosmosToolsPath();
            var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var tool in ToolDefinitions.GetAllTools())
            {
                if (tool is not CommandToolDefinition cmdTool || cmdTool.ReleaseAsset == null)
                {
                    continue;
                }

                if (!processed.Add(cmdTool.ReleaseAsset))
                {
                    continue;
                }

                string targetDir = Path.Combine(toolsPath, cmdTool.ReleaseAsset);
                AnsiConsole.Markup($"  {tool.DisplayName} -> remove {cmdTool.ReleaseAsset}/ ... ");
                bool ok = false;
                if (Directory.Exists(targetDir))
                {
                    try { Directory.Delete(targetDir, true); ok = true; } catch { }
                }
                AnsiConsole.MarkupLine(ok ? "[green]OK[/]" : "[dim]not found[/]");
            }

            // On Windows, remove downloaded tool dirs from user PATH
            if (OperatingSystem.IsWindows())
            {
                AnsiConsole.Markup("  Tools -> remove from PATH ... ");
                bool pathOk = InstallCommand.RemoveToolsFromWindowsPath(toolsPath);
                AnsiConsole.MarkupLine(pathOk ? "[green]OK[/]" : "[dim]nothing to remove[/]");
            }
        }

        if (removePackages)
        {
            // Remove NuGet feed
            AnsiConsole.Markup("  NuGet feed -> remove ... ");
            bool nugetOk = await RunAsync("dotnet", "nuget remove source \"Cosmos Local Feed\"");
            AnsiConsole.MarkupLine(nugetOk ? "[green]OK[/]" : "[dim]not found[/]");

            // Uninstall dotnet tools
            AnsiConsole.Markup("  Cosmos.Patcher -> uninstall ... ");
            bool patcherOk = await RunAsync("dotnet", "tool uninstall -g Cosmos.Patcher");
            AnsiConsole.MarkupLine(patcherOk ? "[green]OK[/]" : "[dim]not installed[/]");

            // Uninstall templates
            AnsiConsole.Markup("  Cosmos.Build.Templates -> uninstall ... ");
            bool templatesOk = await RunAsync("dotnet", "new uninstall Cosmos.Build.Templates");
            AnsiConsole.MarkupLine(templatesOk ? "[green]OK[/]" : "[dim]not installed[/]");

            // Uninstall VS Code extension
            AnsiConsole.Markup("  VS Code extension -> uninstall ... ");
            bool vsCodeOk = await UninstallVSCodeExtensionAsync();
            AnsiConsole.MarkupLine(vsCodeOk ? "[green]OK[/]" : "[dim]not found[/]");
        }

        // Cosmos.Tools uninstalls itself last — print instruction
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("  " + new string('-', 50));
        AnsiConsole.MarkupLine("  [green]Uninstallation complete![/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  To also remove the cosmos CLI itself, run:");
        AnsiConsole.MarkupLine("    [blue]dotnet tool uninstall -g Cosmos.Tools[/]");
        AnsiConsole.WriteLine();

        return 0;
    }

    private static async Task<bool> RunAsync(string fileName, string arguments)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            if (proc == null)
            {
                return false;
            }

            await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            return proc.ExitCode == 0;
        }
        catch { return false; }
    }

    private static async Task<bool> UninstallVSCodeExtensionAsync()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return await RunAsync("cmd", "/c code --uninstall-extension cosmosos.cosmos-vscode");
            }
            else
            {
                return await RunAsync("code", "--uninstall-extension cosmosos.cosmos-vscode");
            }
        }
        catch { return false; }
    }
}
