using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Spectre.Console;
using Spectre.Console.Cli;
using Cosmos.Tools.Platform;

namespace Cosmos.Tools.Commands;

public class BuildSettings : CommandSettings
{
    [CommandOption("-p|--project")]
    [Description("Path to the kernel project (default: current directory)")]
    public string? Project { get; set; }

    [CommandOption("-a|--arch")]
    [Description("Target architecture (x64, arm64). If not specified, builds for detected/default arch")]
    public string? Arch { get; set; }

    [CommandOption("-c|--config")]
    [Description("Build configuration (Debug, Release)")]
    [DefaultValue("Debug")]
    public string Config { get; set; } = "Debug";

    [CommandOption("--all")]
    [Description("Build for all supported architectures")]
    public bool All { get; set; }

    [CommandOption("-v|--verbose")]
    [Description("Show detailed build output")]
    public bool Verbose { get; set; }
}

public class BuildCommand : AsyncCommand<BuildSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, BuildSettings settings)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [bold]Cosmos Kernel Build[/]");
        AnsiConsole.WriteLine("  " + new string('-', 50));

        var csprojPath = FindProjectFile(settings.Project);
        if (csprojPath == null)
        {
            AnsiConsole.MarkupLine("  [red]No .csproj file found in the current directory.[/]");
            AnsiConsole.MarkupLine("  Use [blue]--project[/] to specify the project path.");
            return 1;
        }

        var projectName = Path.GetFileNameWithoutExtension(csprojPath);
        var projectDir = Path.GetDirectoryName(csprojPath)!;

        AnsiConsole.MarkupLine($"  Project: [blue]{projectName}[/]");
        AnsiConsole.MarkupLine($"  Configuration: [blue]{settings.Config}[/]");

        var architectures = new List<string>();
        if (settings.All)
        {
            architectures.Add("x64");
            architectures.Add("arm64");
            AnsiConsole.MarkupLine("  Architectures: [blue]x64, arm64[/]");
        }
        else if (!string.IsNullOrEmpty(settings.Arch))
        {
            architectures.Add(settings.Arch);
            AnsiConsole.MarkupLine($"  Architecture: [blue]{settings.Arch}[/]");
        }
        else
        {
            var detectedArch = DetectArchitecture(csprojPath);
            architectures.Add(detectedArch);
            AnsiConsole.MarkupLine($"  Architecture: [blue]{detectedArch}[/] (detected)");
        }

        AnsiConsole.WriteLine("  " + new string('-', 50));
        AnsiConsole.WriteLine();

        var success = true;
        foreach (var buildArch in architectures)
        {
            var result = await BuildForArchitectureAsync(csprojPath, projectDir, buildArch, settings.Config, settings.Verbose);
            if (!result)
            {
                success = false;
            }
        }

        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("  " + new string('-', 50));

        if (success)
        {
            AnsiConsole.MarkupLine("  [green]Build completed successfully![/]");
            AnsiConsole.WriteLine();
            AnsiConsole.WriteLine("  Output:");
            foreach (var buildArch in architectures)
            {
                var outputDir = Path.Combine(projectDir, $"output-{buildArch}");
                var isoPath = Path.Combine(outputDir, $"{projectName}.iso");
                if (File.Exists(isoPath))
                {
                    AnsiConsole.MarkupLine($"    [blue]{buildArch}[/]: {isoPath}");
                }
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("  Run with: [blue]cosmos run[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("  [red]Build failed![/]");
        }

        AnsiConsole.WriteLine();
        return success ? 0 : 1;
    }

    private static string? FindProjectFile(string? projectPath)
    {
        if (!string.IsNullOrEmpty(projectPath))
        {
            if (File.Exists(projectPath) && projectPath.EndsWith(".csproj"))
                return projectPath;

            if (Directory.Exists(projectPath))
            {
                var files = Directory.GetFiles(projectPath, "*.csproj");
                return files.FirstOrDefault();
            }

            return null;
        }

        var currentDir = Directory.GetCurrentDirectory();
        var csprojFiles = Directory.GetFiles(currentDir, "*.csproj");
        return csprojFiles.FirstOrDefault();
    }

    private static string DetectArchitecture(string csprojPath)
    {
        try
        {
            var content = File.ReadAllText(csprojPath);

            // Check for CosmosTargetArch (set by cosmos new)
            var targetArchMatch = Regex.Match(content, @"<CosmosTargetArch>([^<]+)</CosmosTargetArch>");
            if (targetArchMatch.Success)
            {
                var targetArch = targetArchMatch.Groups[1].Value.Trim().ToLowerInvariant();
                if (targetArch == "x64" || targetArch == "arm64")
                {
                    return targetArch;
                }
            }

            // Check for CosmosArchitectures (multi-arch projects)
            var archMatch = Regex.Match(content, @"<CosmosArchitectures>([^<]+)</CosmosArchitectures>");
            if (archMatch.Success)
            {
                var archs = archMatch.Groups[1].Value.Split(';');
                return archs[0].Trim().ToLowerInvariant();
            }
        }
        catch { }

        // Default to host architecture
        return PlatformInfo.IsArm64 ? "arm64" : "x64";
    }

    private static async Task<bool> BuildForArchitectureAsync(string csprojPath, string projectDir, string arch, string config, bool verbose)
    {
        AnsiConsole.Markup($"  Building for [blue]{arch}[/]... ");

        var runtimeId = arch == "arm64" ? "linux-arm64" : "linux-x64";
        var defineConstants = arch == "arm64" ? "ARCH_ARM64" : "ARCH_X64";
        var outputDir = Path.Combine(projectDir, $"output-{arch}");

        var args = new[]
        {
            "publish",
            csprojPath,
            "-c", config,
            "-r", runtimeId,
            $"-p:DefineConstants={defineConstants}",
            $"-p:CosmosArch={arch}",
            "-o", outputDir,
            verbose ? "--verbosity:normal" : "--verbosity:minimal"
        };

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = string.Join(" ", args),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = projectDir
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                AnsiConsole.MarkupLine("[red]FAILED[/]");
                AnsiConsole.MarkupLine("    Could not start dotnet process");
                return false;
            }

            var output = new List<string>();
            var error = new List<string>();

            var outputTask = Task.Run(async () =>
            {
                string? line;
                while ((line = await process.StandardOutput.ReadLineAsync()) != null)
                {
                    output.Add(line);
                    if (verbose)
                    {
                        AnsiConsole.WriteLine($"    {line}");
                    }
                }
            });

            var errorTask = Task.Run(async () =>
            {
                string? line;
                while ((line = await process.StandardError.ReadLineAsync()) != null)
                {
                    error.Add(line);
                    if (verbose)
                    {
                        AnsiConsole.MarkupLine($"    [yellow]{Markup.Escape(line)}[/]");
                    }
                }
            });

            await Task.WhenAll(outputTask, errorTask);
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                AnsiConsole.MarkupLine("[green]OK[/]");
                return true;
            }
            else
            {
                AnsiConsole.MarkupLine("[red]FAILED[/]");
                AnsiConsole.WriteLine();

                // Show errors from stderr
                var errorLines = error.TakeLast(15).ToList();
                if (errorLines.Count > 0)
                {
                    foreach (var line in errorLines)
                    {
                        AnsiConsole.MarkupLine($"    [red]{Markup.Escape(line)}[/]");
                    }
                }

                // Also show relevant output lines (errors often go to stdout too)
                var relevantOutput = output
                    .Where(l => l.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                               l.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                               l.Contains("NU1", StringComparison.OrdinalIgnoreCase))
                    .TakeLast(10)
                    .ToList();

                if (relevantOutput.Count > 0)
                {
                    foreach (var line in relevantOutput)
                    {
                        AnsiConsole.MarkupLine($"    [yellow]{Markup.Escape(line)}[/]");
                    }
                }

                if (errorLines.Count == 0 && relevantOutput.Count == 0)
                {
                    AnsiConsole.MarkupLine("    [dim]No error details captured. Run with -v for verbose output.[/]");
                }

                return false;
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine("[red]FAILED[/]");
            AnsiConsole.MarkupLine($"    Error: {Markup.Escape(ex.Message)}");
            return false;
        }
    }
}
