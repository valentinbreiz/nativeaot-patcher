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

    [CommandOption("--json")]
    [Description("Output results as JSON (for IDE integration)")]
    public bool Json { get; set; }
}

public class BuildCommand : AsyncCommand<BuildSettings>
{
    private record BuildResult(string Arch, bool Success, string OutputDir, string? IsoPath, string? ElfPath, string Runtime);

    public override async Task<int> ExecuteAsync(CommandContext context, BuildSettings settings)
    {
        if (!settings.Json)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("  [bold]Cosmos Kernel Build[/]");
            AnsiConsole.WriteLine("  " + new string('-', 50));
        }

        var csprojPath = FindProjectFile(settings.Project);
        if (csprojPath == null)
        {
            if (settings.Json)
            {
                PrintJsonError("No .csproj file found in the current directory.");
            }
            else
            {
                AnsiConsole.MarkupLine("  [red]No .csproj file found in the current directory.[/]");
                AnsiConsole.MarkupLine("  Use [blue]--project[/] to specify the project path.");
            }
            return 1;
        }

        var projectName = Path.GetFileNameWithoutExtension(csprojPath);
        var projectDir = Path.GetDirectoryName(csprojPath)!;

        if (!settings.Json)
        {
            AnsiConsole.MarkupLine($"  Project: [blue]{projectName}[/]");
            AnsiConsole.MarkupLine($"  Configuration: [blue]{settings.Config}[/]");
        }

        var architectures = new List<string>();
        if (settings.All)
        {
            architectures.Add("x64");
            architectures.Add("arm64");
            if (!settings.Json) AnsiConsole.MarkupLine("  Architectures: [blue]x64, arm64[/]");
        }
        else if (!string.IsNullOrEmpty(settings.Arch))
        {
            architectures.Add(settings.Arch);
            if (!settings.Json) AnsiConsole.MarkupLine($"  Architecture: [blue]{settings.Arch}[/]");
        }
        else
        {
            var detectedArch = DetectArchitecture(csprojPath);
            architectures.Add(detectedArch);
            if (!settings.Json) AnsiConsole.MarkupLine($"  Architecture: [blue]{detectedArch}[/] (detected)");
        }

        if (!settings.Json)
        {
            AnsiConsole.WriteLine("  " + new string('-', 50));
            AnsiConsole.WriteLine();
        }

        var buildResults = new List<BuildResult>();
        var success = true;

        foreach (var buildArch in architectures)
        {
            var runtimeId = buildArch == "arm64" ? "linux-arm64" : "linux-x64";
            var outputDir = Path.Combine(projectDir, $"output-{buildArch}");
            var result = await BuildForArchitectureAsync(csprojPath, projectDir, buildArch, settings.Config, settings.Verbose, settings.Json);

            string? isoPath = null;
            string? elfPath = null;
            if (result)
            {
                var potentialIso = Path.Combine(outputDir, $"{projectName}.iso");
                var potentialElf = Path.Combine(outputDir, $"{projectName}.elf");
                if (File.Exists(potentialIso)) isoPath = potentialIso;
                if (File.Exists(potentialElf)) elfPath = potentialElf;
            }

            buildResults.Add(new BuildResult(buildArch, result, outputDir, isoPath, elfPath, runtimeId));

            if (!result)
            {
                success = false;
            }
        }

        if (settings.Json)
        {
            PrintJsonResults(projectName, projectDir, settings.Config, buildResults, success);
        }
        else
        {
            AnsiConsole.WriteLine();
            AnsiConsole.WriteLine("  " + new string('-', 50));

            if (success)
            {
                AnsiConsole.MarkupLine("  [green]Build completed successfully![/]");
                AnsiConsole.WriteLine();
                AnsiConsole.WriteLine("  Output:");
                foreach (var result in buildResults)
                {
                    if (result.IsoPath != null)
                    {
                        AnsiConsole.MarkupLine($"    [blue]{result.Arch}[/]: {result.IsoPath}");
                    }
                }
            }
            else
            {
                AnsiConsole.MarkupLine("  [red]Build failed![/]");
            }

            AnsiConsole.WriteLine();
        }

        return success ? 0 : 1;
    }

    private static void PrintJsonError(string message)
    {
        Console.WriteLine("{");
        Console.WriteLine("  \"success\": false,");
        Console.WriteLine($"  \"error\": \"{EscapeJson(message)}\"");
        Console.WriteLine("}");
    }

    private static void PrintJsonResults(string projectName, string projectDir, string config, List<BuildResult> results, bool success)
    {
        Console.WriteLine("{");
        Console.WriteLine($"  \"success\": {success.ToString().ToLower()},");
        Console.WriteLine($"  \"project\": \"{EscapeJson(projectName)}\",");
        Console.WriteLine($"  \"projectDir\": \"{EscapeJson(projectDir)}\",");
        Console.WriteLine($"  \"configuration\": \"{config}\",");
        Console.WriteLine("  \"builds\": [");

        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            var comma = i < results.Count - 1 ? "," : "";
            Console.WriteLine("    {");
            Console.WriteLine($"      \"arch\": \"{r.Arch}\",");
            Console.WriteLine($"      \"success\": {r.Success.ToString().ToLower()},");
            Console.WriteLine($"      \"runtime\": \"{r.Runtime}\",");
            Console.WriteLine($"      \"outputDir\": \"{EscapeJson(r.OutputDir)}\",");
            Console.WriteLine($"      \"isoPath\": {(r.IsoPath != null ? $"\"{EscapeJson(r.IsoPath)}\"" : "null")},");
            Console.WriteLine($"      \"elfPath\": {(r.ElfPath != null ? $"\"{EscapeJson(r.ElfPath)}\"" : "null")}");
            Console.WriteLine($"    }}{comma}");
        }

        Console.WriteLine("  ]");
        Console.WriteLine("}");
    }

    private static string EscapeJson(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
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

    private static async Task<bool> BuildForArchitectureAsync(string csprojPath, string projectDir, string arch, string config, bool verbose, bool json)
    {
        if (!json) AnsiConsole.Markup($"  Building for [blue]{arch}[/]... ");

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
                if (!json)
                {
                    AnsiConsole.MarkupLine("[red]FAILED[/]");
                    AnsiConsole.MarkupLine("    Could not start dotnet process");
                }
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
                    if (verbose && !json)
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
                    if (verbose && !json)
                    {
                        AnsiConsole.MarkupLine($"    [yellow]{Markup.Escape(line)}[/]");
                    }
                }
            });

            await Task.WhenAll(outputTask, errorTask);
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                if (!json) AnsiConsole.MarkupLine("[green]OK[/]");
                return true;
            }
            else
            {
                if (!json)
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
                }

                return false;
            }
        }
        catch (Exception ex)
        {
            if (!json)
            {
                AnsiConsole.MarkupLine("[red]FAILED[/]");
                AnsiConsole.MarkupLine($"    Error: {Markup.Escape(ex.Message)}");
            }
            return false;
        }
    }
}
