using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Cosmos.Tools.Platform;
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

    [CommandOption("--setup <DIR>")]
    [Description("Bundle all tools into DIR for offline installer packaging")]
    public string? Setup { get; set; }
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
        if (settings.Setup != null)
            AnsiConsole.MarkupLine($"  Mode: [blue]Setup bundle[/] -> {Path.GetFullPath(settings.Setup)}");
        AnsiConsole.WriteLine("  " + new string('-', 50));
        AnsiConsole.WriteLine();

        return settings.Setup != null
            ? await BuildSetupAsync(settings)
            : await InstallLocallyAsync(settings);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Local install mode
    // ═══════════════════════════════════════════════════════════════════════

    private static async Task<int> InstallLocallyAsync(InstallSettings settings)
    {
        var results = await ToolChecker.CheckAllToolsAsync(settings.Arch);

        // Warn about tools already on PATH — version mismatches may cause issues
        var foundTools = results.Where(r => r.Found && r.Tool.Name != "dotnet").ToList();
        if (foundTools.Count > 0)
        {
            foreach (var found in foundTools)
            {
                string ver = found.Version != null ? $" (v{found.Version})" : "";
                AnsiConsole.MarkupLine($"  [yellow]WARNING:[/] {found.Tool.DisplayName}{ver} found at [dim]{found.Path}[/]");
                AnsiConsole.MarkupLine("           [yellow]System-installed tools may cause version conflicts we won't support.[/]");
            }
            AnsiConsole.WriteLine();
        }

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
                            packagesToInstall.AddRange(packages);
                        else
                            PrintManualInstruction(status.Tool, info);
                        break;

                    case "download":
                        await DownloadToolAsync(status.Tool, info, ToolChecker.GetCosmosToolsPath());
                        break;

                    case "manual":
                    default:
                        PrintManualInstruction(status.Tool, info);
                        break;
                }
            }

            if (packagesToInstall.Count > 0)
                await InstallPackagesAsync(packageManager, packagesToInstall);
        }

        // Install dotnet tools and templates
        await InstallDotnetToolsAsync();

        // Install VS Code extension
        if (!settings.SkipExtension)
            await InstallVSCodeExtensionAsync();

        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("  " + new string('-', 50));
        AnsiConsole.MarkupLine("  [green]Installation complete![/]");
        AnsiConsole.MarkupLine("  Run [blue]cosmos check[/] to verify installation.");
        AnsiConsole.MarkupLine("  Run [blue]cosmos new[/] to create a new kernel project.");
        AnsiConsole.WriteLine();

        return 0;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Setup bundle mode — downloads ALL tools for offline installer
    // ═══════════════════════════════════════════════════════════════════════

    private static async Task<int> BuildSetupAsync(InstallSettings settings)
    {
        string baseDir = Path.GetFullPath(settings.Setup!);
        string platform = PlatformInfo.CurrentOS switch
        {
            OSPlatform.Windows => "windows",
            OSPlatform.MacOS => "macos",
            _ => "linux"
        };
        string toolsDir = Path.Combine(baseDir, "tools", platform);
        Directory.CreateDirectory(toolsDir);

        var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string packageManager = PlatformInfo.GetPackageManager();
        var packagesToInstall = new List<string>();
        var packageTools = new List<ToolDefinition>();

        // Phase 1: Download tools with DownloadUrl, collect package tools
        foreach (var tool in ToolDefinitions.GetAllTools())
        {
            var info = tool.GetInstallInfo(PlatformInfo.CurrentOS);
            if (info == null) continue;

            string bundleDir = GetBundleDirName(tool);
            if (!processed.Add(bundleDir)) continue;

            string targetDir = Path.Combine(toolsDir, bundleDir);
            if (Directory.Exists(targetDir) && Directory.EnumerateFileSystemEntries(targetDir).Any())
            {
                AnsiConsole.MarkupLine($"  [dim]{tool.DisplayName} -> {bundleDir}/ (exists, skipping)[/]");
                continue;
            }

            if (info.DownloadUrl != null)
            {
                AnsiConsole.Markup($"  {tool.DisplayName} -> {bundleDir}/ ... ");
                bool ok = await DownloadAndExtractAsync(info.DownloadUrl, targetDir);
                AnsiConsole.MarkupLine(ok ? "[green]OK[/]" : "[red]FAILED[/]");
            }
            else if (info.Method == "package")
            {
                var pkgs = GetPackagesForManager(info, packageManager);
                if (pkgs != null)
                {
                    packagesToInstall.AddRange(pkgs);
                    packageTools.Add(tool);
                }
            }
        }

        // Phase 2: Install packages, then copy installed binaries to bundle
        if (packagesToInstall.Count > 0)
        {
            var uniquePkgs = packagesToInstall.Distinct().ToList();
            await InstallPackagesAsync(packageManager, uniquePkgs);

            var copied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tool in packageTools)
            {
                string bundleDir = GetBundleDirName(tool);
                if (!copied.Add(bundleDir)) continue;

                string targetDir = Path.Combine(toolsDir, bundleDir);
                if (Directory.Exists(targetDir) && Directory.EnumerateFileSystemEntries(targetDir).Any())
                    continue;

                AnsiConsole.Markup($"  Bundling {tool.DisplayName} -> {bundleDir}/ ... ");
                bool ok = await CopyInstalledToolAsync(tool, targetDir);
                AnsiConsole.MarkupLine(ok ? "[green]OK[/]" : "[red]FAILED[/]");
            }
        }

        // Phase 3: VS Code extension
        if (!settings.SkipExtension)
            await DownloadVSCodeExtensionToFileAsync(Path.Combine(baseDir, "extensions"));

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [green]Setup bundle complete![/]");
        return 0;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Download & extract helpers
    // ═══════════════════════════════════════════════════════════════════════

    private static async Task DownloadToolAsync(ToolDefinition tool, InstallInfo info, string toolsPath)
    {
        if (string.IsNullOrEmpty(info.DownloadUrl))
        {
            PrintManualInstruction(tool, info);
            return;
        }

        string bundleDir = GetBundleDirName(tool);
        string targetDir = Path.Combine(toolsPath, bundleDir);

        if (Directory.Exists(targetDir) && Directory.EnumerateFileSystemEntries(targetDir).Any())
        {
            AnsiConsole.MarkupLine($"  [dim]{tool.DisplayName} already exists, skipping[/]");
            return;
        }

        AnsiConsole.Markup($"  Downloading [white]{tool.DisplayName}[/] ... ");
        try
        {
            bool ok = await DownloadAndExtractAsync(info.DownloadUrl, targetDir);
            AnsiConsole.MarkupLine(ok ? "[green]OK[/]" : "[red]FAILED[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]FAILED ({Markup.Escape(ex.Message)})[/]");
        }
    }

    private static async Task<bool> DownloadAndExtractAsync(string url, string targetDir)
    {
        try
        {
            Directory.CreateDirectory(targetDir);
            if (url.EndsWith(".git")) return await GitCloneAsync(url, targetDir);
            if (url.EndsWith(".zip")) return await DownloadAndExtractZipAsync(url, targetDir);
            if (url.EndsWith(".7z")) return await DownloadAndExtract7zAsync(url, targetDir);
            return false;
        }
        catch (Exception ex)
        {
            AnsiConsole.Markup($"[red]{Markup.Escape(ex.Message)}[/] ");
            return false;
        }
    }

    private static async Task<bool> DownloadAndExtractZipAsync(string url, string targetDir)
    {
        using var http = CreateHttpClient();
        string tempFile = Path.Combine(Path.GetTempPath(), $"cosmos-{Guid.NewGuid():N}.zip");
        try
        {
            await File.WriteAllBytesAsync(tempFile, await http.GetByteArrayAsync(url));
            ZipFile.ExtractToDirectory(tempFile, targetDir);
            return true;
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    private static async Task<bool> DownloadAndExtract7zAsync(string url, string targetDir)
    {
        using var http = CreateHttpClient();
        string tempFile = Path.Combine(Path.GetTempPath(), $"cosmos-{Guid.NewGuid():N}.7z");
        string tempExtract = Path.Combine(Path.GetTempPath(), $"cosmos-{Guid.NewGuid():N}");
        try
        {
            await File.WriteAllBytesAsync(tempFile, await http.GetByteArrayAsync(url));
            Directory.CreateDirectory(tempExtract);

            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "7z",
                Arguments = $"x \"{tempFile}\" -o\"{tempExtract}\" -y",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (proc == null) return false;
            await proc.WaitForExitAsync();
            if (proc.ExitCode != 0) return false;

            // Flatten: if single top-level directory, move its contents up
            var topDirs = Directory.GetDirectories(tempExtract);
            string source = topDirs.Length == 1 ? topDirs[0] : tempExtract;
            CopyDirectory(source, targetDir);
            return true;
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
            if (Directory.Exists(tempExtract)) Directory.Delete(tempExtract, true);
        }
    }

    private static async Task<bool> GitCloneAsync(string url, string targetDir)
    {
        if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);

        using var proc = Process.Start(new ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"clone \"{url}\" --depth=1 \"{targetDir}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });
        if (proc == null) return false;
        await proc.WaitForExitAsync();
        if (proc.ExitCode != 0) return false;

        string gitDir = Path.Combine(targetDir, ".git");
        if (Directory.Exists(gitDir))
            Directory.Delete(gitDir, true);
        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Package tool bundling — install via package manager, copy to bundle
    // ═══════════════════════════════════════════════════════════════════════

    private static async Task<bool> CopyInstalledToolAsync(ToolDefinition tool, string targetDir)
    {
        string? toolPath = await FindOnPathAsync(tool.Commands);
        if (toolPath == null) return false;

        string sourceDir = Path.GetDirectoryName(toolPath)!;
        Directory.CreateDirectory(targetDir);

        switch (tool.Name)
        {
            case "yasm":
                File.Copy(toolPath, Path.Combine(targetDir, Path.GetFileName(toolPath)), true);
                break;

            case "ld.lld":
                CopyFileIfExists(sourceDir, targetDir, "lld.exe");
                CopyFileIfExists(sourceDir, targetDir, "lld");
                if (!CopyFileIfExists(sourceDir, targetDir, "ld.lld.exe") &&
                    !CopyFileIfExists(sourceDir, targetDir, "ld.lld"))
                {
                    // Create ld.lld as a copy of lld
                    string ext = PlatformInfo.CurrentOS == OSPlatform.Windows ? ".exe" : "";
                    string lldSrc = Path.Combine(sourceDir, $"lld{ext}");
                    if (File.Exists(lldSrc))
                        File.Copy(lldSrc, Path.Combine(targetDir, $"ld.lld{ext}"), true);
                }
                break;

            case "qemu-system-x86_64" or "qemu-system-aarch64":
                string ext2 = PlatformInfo.CurrentOS == OSPlatform.Windows ? ".exe" : "";
                foreach (var name in new[] { $"qemu-system-x86_64{ext2}", $"qemu-system-aarch64{ext2}", $"qemu-img{ext2}" })
                    CopyFileIfExists(sourceDir, targetDir, name);
                foreach (var dll in Directory.GetFiles(sourceDir, "*.dll"))
                    File.Copy(dll, Path.Combine(targetDir, Path.GetFileName(dll)), true);
                string shareDir = Path.Combine(sourceDir, "share");
                if (Directory.Exists(shareDir))
                    CopyDirectory(shareDir, Path.Combine(targetDir, "share"));
                break;

            default:
                File.Copy(toolPath, Path.Combine(targetDir, Path.GetFileName(toolPath)), true);
                break;
        }

        return true;
    }

    private static async Task<string?> FindOnPathAsync(string[] commands)
    {
        string whichCmd = PlatformInfo.CurrentOS == OSPlatform.Windows ? "where" : "which";
        foreach (var cmd in commands)
        {
            try
            {
                using var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = whichCmd,
                    Arguments = cmd,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                });
                if (proc == null) continue;
                string output = await proc.StandardOutput.ReadToEndAsync();
                await proc.WaitForExitAsync();
                string path = output.Split('\n', '\r')[0].Trim();
                if (proc.ExitCode == 0 && File.Exists(path))
                    return path;
            }
            catch { }
        }
        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  VS Code extension
    // ═══════════════════════════════════════════════════════════════════════

    private static async Task<(string? url, string? name)> GetVSCodeExtensionInfoAsync()
    {
        using var http = CreateHttpClient();
        string? token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrEmpty(token))
            http.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        string json = await http.GetStringAsync(
            "https://api.github.com/repos/valentinbreiz/CosmosVsCodeExtension/releases/latest");
        var release = JsonDocument.Parse(json);

        if (release.RootElement.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                string? name = asset.GetProperty("name").GetString();
                if (name?.EndsWith(".vsix") == true)
                    return (asset.GetProperty("browser_download_url").GetString(), name);
            }
        }
        return (null, null);
    }

    private static async Task DownloadVSCodeExtensionToFileAsync(string extensionsDir)
    {
        AnsiConsole.Markup("  VS Code Extension ... ");
        try
        {
            Directory.CreateDirectory(extensionsDir);
            string vsixPath = Path.Combine(extensionsDir, "cosmos-vscode.vsix");
            if (File.Exists(vsixPath))
            {
                AnsiConsole.MarkupLine("[dim]exists, skipping[/]");
                return;
            }

            var (url, name) = await GetVSCodeExtensionInfoAsync();
            if (url == null)
            {
                AnsiConsole.MarkupLine("[yellow]SKIPPED (no .vsix found)[/]");
                return;
            }

            using var http = CreateHttpClient();
            await File.WriteAllBytesAsync(vsixPath, await http.GetByteArrayAsync(url));
            AnsiConsole.MarkupLine("[green]OK[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[yellow]SKIPPED ({Markup.Escape(ex.Message)})[/]");
        }
    }

    private static async Task InstallVSCodeExtensionAsync()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [bold]Installing VS Code Extension[/]");
        AnsiConsole.WriteLine();

        string? codeCommand = GetVSCodeCommand();
        if (codeCommand == null)
        {
            AnsiConsole.MarkupLine("  [yellow]VS Code not found in PATH.[/]");
            AnsiConsole.MarkupLine("  [dim]On macOS: Open VS Code, Cmd+Shift+P, 'Shell Command: Install code command'[/]");
            return;
        }

        AnsiConsole.Markup("  Downloading extension from GitHub... ");
        try
        {
            var (url, name) = await GetVSCodeExtensionInfoAsync();
            if (url == null || name == null)
            {
                AnsiConsole.MarkupLine("[yellow]SKIPPED (no .vsix found)[/]");
                return;
            }
            AnsiConsole.MarkupLine("[green]OK[/]");

            AnsiConsole.Markup($"  Downloading {name}... ");
            using var http = CreateHttpClient();
            byte[] vsixBytes = await http.GetByteArrayAsync(url);
            string tempPath = Path.Combine(Path.GetTempPath(), name);
            await File.WriteAllBytesAsync(tempPath, vsixBytes);
            AnsiConsole.MarkupLine("[green]OK[/]");

            AnsiConsole.Markup("  Installing extension... ");
            ProcessStartInfo psi = OperatingSystem.IsWindows()
                ? new() { FileName = "cmd.exe", Arguments = $"/c {codeCommand} --install-extension \"{tempPath}\" --force", UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true }
                : new() { FileName = codeCommand, Arguments = $"--install-extension \"{tempPath}\" --force", UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };

            using var process = Process.Start(psi);
            if (process == null) { AnsiConsole.MarkupLine("[yellow]SKIPPED[/]"); return; }
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                AnsiConsole.MarkupLine("[green]OK[/]");
                AnsiConsole.MarkupLine("  [dim]Reload VS Code to activate the extension.[/]");
            }
            else
            {
                string error = await process.StandardError.ReadToEndAsync();
                AnsiConsole.MarkupLine("[red]FAILED[/]");
                if (!string.IsNullOrWhiteSpace(error))
                    AnsiConsole.MarkupLine($"  [red]{Markup.Escape(error)}[/]");
            }

            try { File.Delete(tempPath); } catch { }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine("[red]FAILED[/]");
            AnsiConsole.MarkupLine($"  [red]Error: {Markup.Escape(ex.Message)}[/]");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Dotnet tools
    // ═══════════════════════════════════════════════════════════════════════

    private static async Task InstallDotnetToolsAsync()
    {
        AnsiConsole.WriteLine();

        AnsiConsole.Markup("  Installing Cosmos.Patcher... ");
        await InstallDotnetToolAsync("Cosmos.Patcher");

        AnsiConsole.Markup("  Installing Cosmos.Build.Templates... ");
        await InstallTemplateAsync("Cosmos.Build.Templates");
    }

    private static async Task InstallDotnetToolAsync(string packageName)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet", Arguments = $"tool update -g {packageName}",
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
            };
            using var process = Process.Start(psi);
            if (process == null) { AnsiConsole.MarkupLine("[yellow]SKIPPED[/]"); return; }
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                AnsiConsole.MarkupLine("[green]OK[/]");
            }
            else
            {
                psi.Arguments = $"tool install -g {packageName}";
                using var installProcess = Process.Start(psi);
                if (installProcess != null)
                {
                    await installProcess.WaitForExitAsync();
                    AnsiConsole.MarkupLine(installProcess.ExitCode == 0 ? "[green]OK[/]" : "[yellow]SKIPPED[/]");
                }
            }
        }
        catch { AnsiConsole.MarkupLine("[yellow]SKIPPED[/]"); }
    }

    private static async Task InstallTemplateAsync(string packageName)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet", Arguments = $"new install {packageName}",
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
            };
            using var process = Process.Start(psi);
            if (process == null) { AnsiConsole.MarkupLine("[yellow]SKIPPED[/]"); return; }
            await process.WaitForExitAsync();
            AnsiConsole.MarkupLine(process.ExitCode == 0 ? "[green]OK[/]" : "[yellow]SKIPPED[/]");
        }
        catch { AnsiConsole.MarkupLine("[yellow]SKIPPED[/]"); }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Shared helpers
    // ═══════════════════════════════════════════════════════════════════════

    private static string GetBundleDirName(ToolDefinition tool) => tool.Name switch
    {
        "x86_64-elf-gcc" => "x86_64-elf-tools",
        "aarch64-elf-gcc" or "aarch64-elf-as" => "aarch64-elf-tools",
        "qemu-system-x86_64" or "qemu-system-aarch64" => "qemu",
        "ld.lld" => "lld",
        _ => tool.Name
    };

    private static string GetInstallAction(InstallInfo? info)
    {
        if (info == null) return "Manual installation required";
        string packageManager = PlatformInfo.GetPackageManager();
        string[]? packages = GetPackagesForManager(info, packageManager);
        return info.Method switch
        {
            "package" when packages != null => $"{packageManager} install {string.Join(" ", packages)}",
            "download" => $"Download from {info.DownloadUrl}",
            "manual" => info.ManualInstructions ?? "Manual installation required",
            _ => "Manual installation required"
        };
    }

    private static string[]? GetPackagesForManager(InstallInfo info, string packageManager) => packageManager switch
    {
        "apt" => info.AptPackages,
        "dnf" => info.DnfPackages,
        "pacman" => info.PacmanPackages,
        "brew" => info.BrewPackages,
        "choco" => info.ChocoPackages,
        _ => null
    };

    private static async Task InstallPackagesAsync(string packageManager, List<string> packages)
    {
        if (packages.Count == 0) return;

        AnsiConsole.MarkupLine($"  Installing packages via [blue]{packageManager}[/]...");
        AnsiConsole.WriteLine();

        var (command, args) = packageManager switch
        {
            "apt" => ("sudo", $"apt-get install -y {string.Join(" ", packages)}"),
            "dnf" => ("sudo", $"dnf install -y {string.Join(" ", packages)}"),
            "pacman" => ("sudo", $"pacman -S --noconfirm {string.Join(" ", packages)}"),
            "brew" => ("brew", $"install {string.Join(" ", packages)}"),
            "choco" => ("choco", $"install -y --no-progress {string.Join(" ", packages)}"),
            _ => throw new InvalidOperationException($"Unknown package manager: {packageManager}")
        };

        AnsiConsole.MarkupLine($"  [dim]$ {command} {args}[/]");
        AnsiConsole.WriteLine();

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = command, Arguments = args,
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
            });
            if (process == null) { AnsiConsole.MarkupLine("  [red]Failed to start package manager[/]"); return; }

            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (!string.IsNullOrWhiteSpace(output))
                foreach (string line in output.Split('\n').Take(20))
                    AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(line)}[/]");

            if (process.ExitCode == 0)
                AnsiConsole.MarkupLine("  [green]Packages installed successfully[/]");
            else
            {
                AnsiConsole.MarkupLine($"  [red]Package installation failed (exit code: {process.ExitCode})[/]");
                if (!string.IsNullOrWhiteSpace(error))
                    AnsiConsole.MarkupLine($"  [red]{Markup.Escape(error)}[/]");
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"  [red]Error: {Markup.Escape(ex.Message)}[/]");
        }
    }

    private static bool CopyFileIfExists(string sourceDir, string targetDir, string fileName)
    {
        string src = Path.Combine(sourceDir, fileName);
        if (!File.Exists(src)) return false;
        File.Copy(src, Path.Combine(targetDir, fileName), true);
        return true;
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
        foreach (string dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
    }

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient();
        http.DefaultRequestHeaders.Add("User-Agent", "Cosmos-Tools");
        return http;
    }

    private static string? GetVSCodeCommand()
    {
        bool isWindows = OperatingSystem.IsWindows();
        string[] commands = isWindows
            ? ["code.cmd", "code", "code-insiders.cmd", "code-insiders", "codium.cmd", "codium"]
            : ["code", "code-insiders", "codium"];

        foreach (string cmd in commands)
        {
            try
            {
                ProcessStartInfo psi = isWindows
                    ? new() { FileName = "cmd.exe", Arguments = $"/c {cmd} --version", UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true }
                    : new() { FileName = cmd, Arguments = "--version", UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    process.WaitForExit(3000);
                    if (process.ExitCode == 0) return cmd;
                }
            }
            catch { }
        }
        return null;
    }

    private static void PrintManualInstruction(ToolDefinition tool, InstallInfo? info = null)
    {
        AnsiConsole.MarkupLine($"  [yellow]Manual installation required for {tool.DisplayName}:[/]");
        if (info?.ManualInstructions != null)
            AnsiConsole.MarkupLine($"    {info.ManualInstructions}");
        else if (info?.DownloadUrl != null)
            AnsiConsole.MarkupLine($"    Download from: {info.DownloadUrl}");
        else
            AnsiConsole.MarkupLine($"    Please install {tool.Name} manually.");
        AnsiConsole.WriteLine();
    }
}
