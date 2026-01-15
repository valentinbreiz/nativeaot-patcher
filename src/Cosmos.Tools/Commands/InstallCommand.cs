using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Cosmos.Tools.Platform;
using SharpCompress.Archives;
using SharpCompress.Common;
using Spectre.Console;
using Spectre.Console.Cli;

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

    [CommandOption("--skip-extension")]
    [Description("Skip VS Code extension installation")]
    public bool SkipExtension { get; set; }
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
                string action = GetInstallAction(info);
                installPlan.Add((missing, info, action));

                string required = missing.Tool.Required ? "" : " [dim](optional)[/]";
                AnsiConsole.MarkupLine($"  - [white]{missing.Tool.DisplayName}[/]{required}");
                AnsiConsole.MarkupLine($"    [cyan]{action}[/]");
            }

            AnsiConsole.WriteLine();

            if (!settings.Auto)
            {
                bool proceed = AnsiConsole.Confirm("  Proceed with installation?", true);
                if (!proceed)
                {
                    AnsiConsole.WriteLine("  Installation cancelled.");
                    return 0;
                }
            }

            AnsiConsole.WriteLine();

            string packageManager = PlatformInfo.GetPackageManager();
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
                        string[]? packages = GetPackagesForManager(info, packageManager);
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

        // Install VS Code extension by default
        if (!settings.SkipExtension)
        {
            await InstallVSCodeExtensionAsync();
        }

        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("  " + new string('-', 50));
        AnsiConsole.MarkupLine("  [green]Installation complete![/]");
        AnsiConsole.MarkupLine("  Run [blue]cosmos check[/] to verify installation.");
        AnsiConsole.MarkupLine("  Run [blue]cosmos new[/] to create a new kernel project.");
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

    private static async Task InstallVSCodeExtensionAsync()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [bold]Installing VS Code Extension[/]");
        AnsiConsole.WriteLine();

        // Check if 'code' command is available
        string? codeCommand = GetVSCodeCommand();
        if (codeCommand == null)
        {
            AnsiConsole.MarkupLine("  [yellow]VS Code not found in PATH.[/]");
            AnsiConsole.MarkupLine("  [yellow]Please install VS Code and ensure 'code' command is available.[/]");
            AnsiConsole.MarkupLine("  [dim]On macOS: Open VS Code, Cmd+Shift+P, 'Shell Command: Install code command'[/]");
            return;
        }

        AnsiConsole.Markup("  Downloading extension from GitHub... ");

        try
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Cosmos-Tools");

            // Get latest release from GitHub API
            string releaseUrl = "https://api.github.com/repos/valentinbreiz/CosmosVsCodeExtension/releases/latest";
            string releaseResponse = await httpClient.GetStringAsync(releaseUrl);
            var releaseJson = JsonDocument.Parse(releaseResponse);

            string? vsixUrl = null;
            string? vsixName = null;

            // Find .vsix asset in release
            if (releaseJson.RootElement.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    string? name = asset.GetProperty("name").GetString();
                    if (name != null && name.EndsWith(".vsix"))
                    {
                        vsixUrl = asset.GetProperty("browser_download_url").GetString();
                        vsixName = name;
                        break;
                    }
                }
            }

            if (vsixUrl == null)
            {
                AnsiConsole.MarkupLine("[yellow]SKIPPED[/]");
                AnsiConsole.MarkupLine("  [yellow]No .vsix file found in latest release.[/]");
                return;
            }

            AnsiConsole.MarkupLine("[green]OK[/]");

            // Download the .vsix file
            AnsiConsole.Markup($"  Downloading {vsixName}... ");
            byte[] vsixBytes = await httpClient.GetByteArrayAsync(vsixUrl);

            string tempPath = Path.Combine(Path.GetTempPath(), vsixName!);
            await File.WriteAllBytesAsync(tempPath, vsixBytes);
            AnsiConsole.MarkupLine("[green]OK[/]");

            // Install the extension
            AnsiConsole.Markup("  Installing extension... ");
            ProcessStartInfo psi;

            if (OperatingSystem.IsWindows())
            {
                psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {codeCommand} --install-extension \"{tempPath}\" --force",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
            }
            else
            {
                psi = new ProcessStartInfo
                {
                    FileName = codeCommand,
                    Arguments = $"--install-extension \"{tempPath}\" --force",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
            }

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
                AnsiConsole.MarkupLine("  [green]VS Code extension installed successfully![/]");
                AnsiConsole.MarkupLine("  [dim]Reload VS Code to activate the extension.[/]");
            }
            else
            {
                string error = await process.StandardError.ReadToEndAsync();
                AnsiConsole.MarkupLine("[red]FAILED[/]");
                if (!string.IsNullOrWhiteSpace(error))
                {
                    AnsiConsole.MarkupLine($"  [red]{Markup.Escape(error)}[/]");
                }
            }

            // Clean up temp file
            try { File.Delete(tempPath); } catch { }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine("[red]FAILED[/]");
            AnsiConsole.MarkupLine($"  [red]Error: {Markup.Escape(ex.Message)}[/]");
        }
    }

    private static string? GetVSCodeCommand()
    {
        // On Windows, VS Code is installed as code.cmd, so we need to check .cmd variants too
        bool isWindows = OperatingSystem.IsWindows();
        string[] commands = isWindows
            ? new[] { "code.cmd", "code", "code-insiders.cmd", "code-insiders", "codium.cmd", "codium" }
            : new[] { "code", "code-insiders", "codium" };

        foreach (string? cmd in commands)
        {
            try
            {
                ProcessStartInfo psi;

                if (isWindows)
                {
                    // On Windows, use cmd /c to properly resolve .cmd files in PATH
                    psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c {cmd} --version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                }
                else
                {
                    psi = new ProcessStartInfo
                    {
                        FileName = cmd,
                        Arguments = "--version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                }

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

    private static string GetInstallAction(InstallInfo? info)
    {
        if (info == null)
            return "Manual installation required";

        string packageManager = PlatformInfo.GetPackageManager();
        string[]? packages = GetPackagesForManager(info, packageManager);

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

            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (!string.IsNullOrWhiteSpace(output))
            {
                foreach (string? line in output.Split('\n').Take(20))
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

        string toolsPath = ToolChecker.GetCosmosToolsPath();
        Directory.CreateDirectory(toolsPath);

        // Determine destination folder (avoid cluttering root tools folder)
        // For simple exes like xorriso, we can put them in a 'bin' or 'xorriso' folder
        // For archives, we usually extract to a folder named after the tool
        string installDir = Path.Combine(toolsPath, tool.Name);
        Directory.CreateDirectory(installDir);

        string fileName = Path.GetFileName(new Uri(info.DownloadUrl).LocalPath);
        string tempFile = Path.Combine(Path.GetTempPath(), fileName);

        try
        {
            using var client = new HttpClient();
            AnsiConsole.Markup($"  Fetching {fileName}... ");
            var data = await client.GetByteArrayAsync(info.DownloadUrl);
            await File.WriteAllBytesAsync(tempFile, data);
            AnsiConsole.MarkupLine("[green]OK[/]");

            AnsiConsole.Markup("  Installing... ");

            if (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                // Just copy the exe
                string destFile = Path.Combine(installDir, fileName);
                File.Copy(tempFile, destFile, true);
                
                // Add to PATH
                await AddToPathAsync(installDir);
            }
            else if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || 
                     fileName.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ||
                     fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            {
                // Extract
                using var stream = File.OpenRead(tempFile);
                using var archive = SharpCompress.Archives.ArchiveFactory.Open(stream);
                
                foreach (var entry in archive.Entries)
                {
                    if (!entry.IsDirectory)
                    {
                        entry.WriteToDirectory(installDir, new SharpCompress.Common.ExtractionOptions 
                        {
                            ExtractFullPath = true,
                            Overwrite = true
                        });
                    }
                }
                
                // Find 'bin' directory if it exists, otherwise use installDir
                string binPath = installDir;
                string[] subDirs = Directory.GetDirectories(installDir, "bin", SearchOption.AllDirectories);
                if (subDirs.Length > 0)
                {
                    // Use the first bin folder found (common in gcc toolchains)
                    binPath = subDirs[0]; 
                }
                
                await AddToPathAsync(binPath);
            }

            AnsiConsole.MarkupLine("[green]OK[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine("[red]FAILED[/]");
            AnsiConsole.MarkupLine($"  [red]Error: {Markup.Escape(ex.Message)}[/]");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    private static async Task AddToPathAsync(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            string? currentPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User);
            if (currentPath == null || !currentPath.Split(';').Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                string newPath = string.IsNullOrEmpty(currentPath) ? path : currentPath + ";" + path;
                Environment.SetEnvironmentVariable("PATH", newPath, EnvironmentVariableTarget.User);
                AnsiConsole.MarkupLine($"  [dim]Added {path} to User PATH[/]");
            }
        }
        else
        {
            // Linux/macOS
            string shell = Environment.GetEnvironmentVariable("SHELL") ?? "/bin/bash";
            string rcFile = shell.EndsWith("zsh") ? ".zshrc" : ".bashrc";
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string rcPath = Path.Combine(home, rcFile);
            
            string exportCmd = $"export PATH=\"{path}:$PATH\"";
            
            if (File.Exists(rcPath))
            {
                string content = await File.ReadAllTextAsync(rcPath);
                if (!content.Contains(path))
                {
                    await File.AppendAllTextAsync(rcPath, $"\n# Cosmos Tools\n{exportCmd}\n");
                    AnsiConsole.MarkupLine($"  [dim]Added to {rcFile}. Please restart shell.[/]");
                }
            }
            else
            {
                 AnsiConsole.MarkupLine($"  [yellow]Could not find shell config file. Please add this manually:[/]");
                 AnsiConsole.MarkupLine($"  [cyan]{exportCmd}[/]");
            }
        }
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
