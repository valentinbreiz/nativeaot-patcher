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
    [CommandOption("-y|--auto")]
    [Description("Automatically install without prompting")]
    public bool Auto { get; set; }

    [CommandOption("--setup <DIR>")]
    [Description("Bundle all tools into DIR for offline installer packaging")]
    public string? Setup { get; set; }

    [CommandOption("--skip-tools")]
    [Description("Skip system tools installation")]
    public bool SkipTools { get; set; }
}

public class InstallCommand : AsyncCommand<InstallSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, InstallSettings settings)
    {
        string mode = settings.Setup != null
            ? $"Setup bundle -> {Path.GetFullPath(settings.Setup)}"
            : "Local install";
        CommandHelper.PrintHeader("Cosmos Tools Installer", mode);

        if (!settings.Auto)
        {
            bool proceed = AnsiConsole.Confirm("  Proceed with installation?", false);
            if (!proceed)
            {
                AnsiConsole.WriteLine("  Installation cancelled.");
                return 0;
            }
            AnsiConsole.WriteLine();
        }

        return settings.Setup != null
            ? await BuildSetupAsync(settings)
            : await InstallLocallyAsync(settings);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Local install mode
    // ═══════════════════════════════════════════════════════════════════════

    private static async Task<int> InstallLocallyAsync(InstallSettings settings)
    {
        // Install system tools
        if (!settings.SkipTools)
        {
            string packageManager = PlatformInfo.GetPackageManager();
            var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool hasDownloadedTools = false;

            foreach (var tool in ToolDefinitions.GetAllTools())
            {
                if (tool is not CommandToolDefinition cmdTool)
                {
                    continue;
                }

                var info = cmdTool.GetInstallInfo(PlatformInfo.CurrentOS);
                if (info == null)
                {
                    continue;
                }

                string bundleDir = GetBundleDirName(tool);
                string dedupKey = info.DownloadUrl != null
                    ? $"dl:{bundleDir}"
                    : $"pkg:{string.Join(",", GetPackagesForManager(info, packageManager) ?? [])}";
                if (!processed.Add(dedupKey))
                {
                    continue;
                }

                var status = await ToolChecker.CheckToolAsync(tool);
                if (status.Found)
                {
                    string ver = status.Version != null ? $" (v{status.Version})" : "";
                    AnsiConsole.MarkupLine($"  [dim]{tool.DisplayName}{ver} found at {status.Path}[/]");
                    continue;
                }

                if (info.DownloadUrl != null)
                {
                    string toolsPath = ToolChecker.GetCosmosToolsPath();
                    string targetDir = Path.Combine(toolsPath, bundleDir);
                    AnsiConsole.Markup($"  {tool.DisplayName} -> {bundleDir}/ ... ");
                    bool ok = await DownloadAndExtractAsync(info.DownloadUrl, targetDir);
                    AnsiConsole.MarkupLine(ok ? "[green]OK[/]" : "[red]FAILED[/]");
                    if (ok)
                    {
                        hasDownloadedTools = true;
                    }
                }
                else if (info.Method == "package")
                {
                    var pkgs = GetPackagesForManager(info, packageManager);
                    if (pkgs != null)
                    {
                        AnsiConsole.Markup($"  {tool.DisplayName} -> {packageManager} ... ");
                        bool ok = await RunPackageManagerAsync(packageManager, pkgs);
                        AnsiConsole.MarkupLine(ok ? "[green]OK[/]" : "[red]FAILED[/]");
                    }
                }
            }

            // On Windows, add downloaded tool dirs to user PATH
            if (hasDownloadedTools && OperatingSystem.IsWindows())
            {
                AnsiConsole.Markup("  Tools -> PATH ... ");
                bool pathOk = AddToolsToWindowsPath(ToolChecker.GetCosmosToolsPath());
                AnsiConsole.MarkupLine(pathOk ? "[green]OK[/]" : "[yellow]SKIPPED[/]");
            }

            // In CI, propagate tool paths to subsequent steps
            await PropagateToolPathsForCIAsync();
        }

        // Install dotnet tools and templates
        await InstallDotnetToolsAsync();

        // Install VS Code extension
        await InstallVSCodeExtensionAsync();

        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("  " + new string('-', 50));
        AnsiConsole.MarkupLine("  [green]Installation complete![/]");
        AnsiConsole.WriteLine();
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
        if (!OperatingSystem.IsWindows())
        {
            AnsiConsole.MarkupLine("  [red]Setup mode is only supported on Windows.[/]");
            return 1;
        }

        string baseDir = Path.GetFullPath(settings.Setup!);
        string platform = "windows";
        string toolsDir = Path.Combine(baseDir, "tools", platform);
        Directory.CreateDirectory(toolsDir);

        var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string packageManager = PlatformInfo.GetPackageManager();

        // Download or install each tool
        foreach (var tool in ToolDefinitions.GetAllTools())
        {
            if (tool is not CommandToolDefinition cmdTool)
            {
                continue;
            }

            var info = cmdTool.GetInstallInfo(PlatformInfo.CurrentOS);
            if (info == null)
            {
                continue;
            }

            string bundleDir = GetBundleDirName(tool);
            if (!processed.Add(bundleDir))
            {
                continue;
            }

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
                    AnsiConsole.Markup($"  {tool.DisplayName} -> {bundleDir}/ ... ");
                    bool ok = await InstallPackageAndCopyAsync(packageManager, pkgs, tool, targetDir);
                    AnsiConsole.MarkupLine(ok ? "[green]OK[/]" : "[red]FAILED[/]");
                }
            }
        }

        // Stage dotnet tool packages
        AnsiConsole.Markup("  Dotnet tool packages -> dotnet-tools/ ... ");
        bool stagedTools = StageDotnetToolPackages(baseDir);
        AnsiConsole.MarkupLine(stagedTools ? "[green]OK[/]" : "[yellow]SKIPPED[/]");

        // VS Code extension
        await DownloadVSCodeExtensionToFileAsync(Path.Combine(baseDir, "extensions"));

        // Build installer (Windows only, requires Inno Setup)
        if (OperatingSystem.IsWindows())
        {
            string issFile = Path.Combine(Path.GetDirectoryName(baseDir)!, "Cosmos.iss");
            if (File.Exists(issFile))
            {
                AnsiConsole.Markup("  Windows installer -> iscc ... ");
                bool ok = await BuildInnoSetupAsync(issFile, baseDir);
                AnsiConsole.MarkupLine(ok ? "[green]OK[/]" : "[red]FAILED[/]");
            }
        }

        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("  " + new string('-', 50));
        AnsiConsole.MarkupLine("  [green]Setup bundle complete![/]");
        AnsiConsole.WriteLine();
        return 0;
    }

    private static async Task<bool> BuildInnoSetupAsync(string issFile, string baseDir)
    {
        // Detect version from Cosmos.SDK package
        string packagesDir = Path.Combine(baseDir, "packages");
        string? version = null;
        if (Directory.Exists(packagesDir))
        {
            var sdkPkg = Directory.GetFiles(packagesDir, "Cosmos.SDK.*.nupkg").FirstOrDefault();
            if (sdkPkg != null)
            {
                string name = Path.GetFileNameWithoutExtension(sdkPkg);
                version = name["Cosmos.SDK.".Length..];
            }
        }

        var psi = new ProcessStartInfo
        {
            FileName = "iscc",
            Arguments = $"\"{issFile}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (version != null)
        {
            psi.Environment["COSMOS_VERSION"] = version;
        }

        using var proc = Process.Start(psi);
        if (proc == null)
        {
            return false;
        }

        await proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();
        return proc.ExitCode == 0;
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
            if (url.EndsWith(".git"))
            {
                return await GitCloneAsync(url, targetDir);
            }

            if (url.EndsWith(".zip"))
            {
                return await DownloadAndExtractZipAsync(url, targetDir);
            }

            if (url.EndsWith(".7z"))
            {
                return await DownloadAndExtract7zAsync(url, targetDir);
            }

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
            if (proc == null)
            {
                return false;
            }

            await proc.WaitForExitAsync();
            if (proc.ExitCode != 0)
            {
                return false;
            }

            // Flatten: if single top-level directory, move its contents up
            var topDirs = Directory.GetDirectories(tempExtract);
            string source = topDirs.Length == 1 ? topDirs[0] : tempExtract;
            CopyDirectory(source, targetDir);
            return true;
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }

            if (Directory.Exists(tempExtract))
            {
                Directory.Delete(tempExtract, true);
            }
        }
    }

    private static async Task<bool> GitCloneAsync(string url, string targetDir)
    {
        if (Directory.Exists(targetDir))
        {
            Directory.Delete(targetDir, true);
        }

        using var proc = Process.Start(new ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"clone \"{url}\" --depth=1 \"{targetDir}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });
        if (proc == null)
        {
            return false;
        }

        await proc.WaitForExitAsync();
        if (proc.ExitCode != 0)
        {
            return false;
        }

        // Remove .git directory — on Windows, pack files may be locked,
        // so use rmdir which handles this better than Directory.Delete
        string gitDir = Path.Combine(targetDir, ".git");
        if (Directory.Exists(gitDir))
        {
            if (OperatingSystem.IsWindows())
            {
                using var rm = Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c rmdir /s /q \"{gitDir}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                });
                if (rm != null)
                {
                    await rm.WaitForExitAsync();
                }
            }
            else
            {
                Directory.Delete(gitDir, true);
            }
        }

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Package tool bundling — install via package manager, copy to bundle
    // ═══════════════════════════════════════════════════════════════════════

    private static async Task<bool> InstallPackageAndCopyAsync(string packageManager, string[] packages, ToolDefinition tool, string targetDir)
    {
        try
        {
            bool installed = await RunPackageManagerAsync(packageManager, packages);
            if (!installed)
            {
                return false;
            }

            return await CopyInstalledToolAsync(tool, targetDir);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> CopyInstalledToolAsync(ToolDefinition tool, string targetDir)
    {
        var status = await ToolChecker.CheckToolAsync(tool);
        string? toolPath = status.Path;
        if (toolPath == null)
        {
            return false;
        }

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
                    {
                        File.Copy(lldSrc, Path.Combine(targetDir, $"ld.lld{ext}"), true);
                    }
                }
                break;

            case "qemu-system-x86_64" or "qemu-system-aarch64":
                string ext2 = PlatformInfo.CurrentOS == OSPlatform.Windows ? ".exe" : "";
                foreach (var name in new[] { $"qemu-system-x86_64{ext2}", $"qemu-system-aarch64{ext2}", $"qemu-img{ext2}" })
                {
                    CopyFileIfExists(sourceDir, targetDir, name);
                }

                foreach (var dll in Directory.GetFiles(sourceDir, "*.dll"))
                {
                    File.Copy(dll, Path.Combine(targetDir, Path.GetFileName(dll)), true);
                }

                string shareDir = Path.Combine(sourceDir, "share");
                if (Directory.Exists(shareDir))
                {
                    CopyDirectory(shareDir, Path.Combine(targetDir, "share"));
                }

                break;

            default:
                File.Copy(toolPath, Path.Combine(targetDir, Path.GetFileName(toolPath)), true);
                break;
        }

        return true;
    }

    private static bool StageDotnetToolPackages(string baseDir)
    {
        string packagesDir = Path.Combine(baseDir, "packages");
        if (!Directory.Exists(packagesDir))
        {
            return false;
        }

        string dotnetToolsDir = Path.Combine(baseDir, "dotnet-tools");
        Directory.CreateDirectory(dotnetToolsDir);

        string[] toolPackages = ["Cosmos.Patcher", "Cosmos.Tools", "Cosmos.Build.Templates"];
        int copied = 0;
        foreach (string toolName in toolPackages)
        {
            foreach (string file in Directory.GetFiles(packagesDir, $"{toolName}.*.nupkg"))
            {
                File.Copy(file, Path.Combine(dotnetToolsDir, Path.GetFileName(file)), true);
                copied++;
            }
        }

        return copied > 0;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  VS Code extension
    // ═══════════════════════════════════════════════════════════════════════

    private static async Task<(string? url, string? name)> GetVSCodeExtensionInfoAsync()
    {
        using var http = CreateHttpClient();
        string? token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrEmpty(token))
        {
            http.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        }

        string json = await http.GetStringAsync(
            "https://api.github.com/repos/valentinbreiz/CosmosVsCodeExtension/releases/latest");
        var release = JsonDocument.Parse(json);

        if (release.RootElement.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                string? name = asset.GetProperty("name").GetString();
                if (name?.EndsWith(".vsix") == true)
                {
                    return (asset.GetProperty("browser_download_url").GetString(), name);
                }
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
        string? codeCommand = GetVSCodeCommand();
        if (codeCommand == null)
        {
            AnsiConsole.MarkupLine("  [yellow]VS Code not found in PATH.[/]");
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
                FileName = "dotnet",
                Arguments = $"tool update -g {packageName}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
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
                FileName = "dotnet",
                Arguments = $"new install {packageName}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
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

    internal static string GetBundleDirName(ToolDefinition tool) => tool.Name switch
    {
        "x86_64-elf-gcc" => "x86_64-elf-tools",
        "aarch64-elf-gcc" or "aarch64-elf-as" => "aarch64-elf-tools",
        "qemu-system-x86_64" or "qemu-system-aarch64" => "qemu",
        "ld.lld" => "lld",
        _ => tool.Name
    };

    private static string GetInstallAction(InstallInfo? info)
    {
        if (info == null)
        {
            return "Manual installation required";
        }

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

    internal static string[]? GetPackagesForManager(InstallInfo info, string packageManager) => packageManager switch
    {
        "apt" => info.AptPackages,
        "dnf" => info.DnfPackages,
        "pacman" => info.PacmanPackages,
        "brew" => info.BrewPackages,
        "choco" => info.ChocoPackages,
        _ => null
    };

    private static (string command, string args) GetPackageManagerCommand(string packageManager, IEnumerable<string> packages) => packageManager switch
    {
        "apt" => ("sudo", $"apt-get install -y {string.Join(" ", packages)}"),
        "dnf" => ("sudo", $"dnf install -y {string.Join(" ", packages)}"),
        "pacman" => ("sudo", $"pacman -S --noconfirm {string.Join(" ", packages)}"),
        "brew" => ("brew", $"install {string.Join(" ", packages)}"),
        "choco" => ("choco", $"install -y --no-progress {string.Join(" ", packages)}"),
        _ => throw new InvalidOperationException($"Unknown package manager: {packageManager}")
    };

    internal static (string command, string args) GetPackageManagerUninstallCommand(string packageManager, IEnumerable<string> packages) => packageManager switch
    {
        "apt" => ("sudo", $"apt-get remove -y {string.Join(" ", packages)}"),
        "dnf" => ("sudo", $"dnf remove -y {string.Join(" ", packages)}"),
        "pacman" => ("sudo", $"pacman -R --noconfirm {string.Join(" ", packages)}"),
        "brew" => ("brew", $"uninstall {string.Join(" ", packages)}"),
        "choco" => ("choco", $"uninstall -y --no-progress {string.Join(" ", packages)}"),
        _ => throw new InvalidOperationException($"Unknown package manager: {packageManager}")
    };

    private static async Task<bool> RunPackageManagerAsync(string packageManager, IEnumerable<string> packages)
    {
        var (command, args) = GetPackageManagerCommand(packageManager, packages);
        using var proc = Process.Start(new ProcessStartInfo
        {
            FileName = command,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });
        if (proc == null)
        {
            return false;
        }

        await proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();
        return proc.ExitCode == 0;
    }



    private static bool CopyFileIfExists(string sourceDir, string targetDir, string fileName)
    {
        string src = Path.Combine(sourceDir, fileName);
        if (!File.Exists(src))
        {
            return false;
        }

        File.Copy(src, Path.Combine(targetDir, fileName), true);
        return true;
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
        }

        foreach (string dir in Directory.GetDirectories(source))
        {
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
        }
    }

    private static async Task PropagateToolPathsForCIAsync()
    {
        string? githubPath = Environment.GetEnvironmentVariable("GITHUB_PATH");
        if (githubPath == null)
        {
            return;
        }

        var addedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in ToolDefinitions.GetAllTools())
        {
            if (tool is not CommandToolDefinition cmdTool)
            {
                continue;
            }

            var status = await ToolChecker.CheckToolAsync(cmdTool);
            if (status.Path == null)
            {
                continue;
            }

            string? dir = Path.GetDirectoryName(status.Path);
            if (dir != null && addedDirs.Add(dir))
            {
                File.AppendAllText(githubPath, dir + "\n");
            }
        }
    }

    // Tool directories that should be on PATH (matches ISS CurStepChanged)
    private static readonly string[] ToolPathSubDirs =
    [
        "yasm", "xorriso", "lld",
        Path.Combine("x86_64-elf-tools", "bin"),
        Path.Combine("aarch64-elf-tools", "bin"),
        "qemu"
    ];

    internal static bool AddToolsToWindowsPath(string toolsPath)
    {
        try
        {
            string currentPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
            bool changed = false;

            foreach (string sub in ToolPathSubDirs)
            {
                string dir = Path.Combine(toolsPath, sub);
                if (!currentPath.Contains(dir, StringComparison.OrdinalIgnoreCase))
                {
                    currentPath = currentPath.Length > 0 ? $"{currentPath};{dir}" : dir;
                    changed = true;
                }
            }

            if (changed)
            {
                Environment.SetEnvironmentVariable("PATH", currentPath, EnvironmentVariableTarget.User);
            }

            return true;
        }
        catch { return false; }
    }

    internal static bool RemoveToolsFromWindowsPath(string toolsPath)
    {
        try
        {
            string currentPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
            bool changed = false;
            foreach (string sub in ToolPathSubDirs)
            {
                string dir = Path.Combine(toolsPath, sub);
                string upper = currentPath.ToUpperInvariant();
                string upperDir = dir.ToUpperInvariant();
                int idx = upper.IndexOf(upperDir);
                if (idx >= 0)
                {
                    currentPath = currentPath.Remove(idx, dir.Length);
                    // Clean up double semicolons or leading/trailing semicolons
                    currentPath = currentPath.Replace(";;", ";").Trim(';');
                    changed = true;
                }
            }
            if (changed)
            {
                Environment.SetEnvironmentVariable("PATH", currentPath, EnvironmentVariableTarget.User);
            }

            return true;
        }
        catch { return false; }
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
