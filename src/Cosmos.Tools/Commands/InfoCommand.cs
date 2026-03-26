using System.ComponentModel;
using System.Runtime.InteropServices;
using Cosmos.Tools.Platform;
using Spectre.Console;
using Spectre.Console.Cli;
using SysOSPlatform = System.Runtime.InteropServices.OSPlatform;

namespace Cosmos.Tools.Commands;

public class InfoSettings : CommandSettings
{
    [CommandOption("--json")]
    [Description("Output as JSON (for IDE integration)")]
    public bool Json { get; set; }
}

public class InfoCommand : Command<InfoSettings>
{
    public override int Execute(CommandContext context, InfoSettings settings)
    {
        string platform = GetPlatformName();
        string arch = PlatformInfo.CurrentArch.ToString().ToLower();
        string packageManager = PlatformInfo.GetPackageManager();
        string displayBackend = GetDisplayBackend();
        string gdbX64 = GetGdbCommand("x64");
        string gdbArm64 = GetGdbCommand("arm64");
        if (settings.Json)
        {
            Console.WriteLine("{");
            Console.WriteLine($"  \"platform\": \"{platform}\",");
            Console.WriteLine($"  \"platformName\": \"{PlatformInfo.GetDistroName()}\",");
            Console.WriteLine($"  \"arch\": \"{arch}\",");
            Console.WriteLine($"  \"packageManager\": \"{packageManager}\",");
            Console.WriteLine($"  \"qemuDisplay\": \"{displayBackend}\",");
            Console.WriteLine($"  \"gdbX64Command\": \"{EscapeJson(gdbX64)}\",");
            Console.WriteLine($"  \"gdbArm64Command\": \"{EscapeJson(gdbArm64)}\"");
            Console.WriteLine("}");
        }
        else
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("  [bold]Cosmos Tools Info[/]");
            AnsiConsole.WriteLine("  " + new string('-', 50));
            AnsiConsole.MarkupLine($"  Platform: [blue]{PlatformInfo.GetDistroName()}[/] ({platform})");
            AnsiConsole.MarkupLine($"  Architecture: [blue]{arch}[/]");
            AnsiConsole.MarkupLine($"  Package Manager: [blue]{packageManager}[/]");
            AnsiConsole.MarkupLine($"  QEMU Display: [blue]{displayBackend}[/]");
            AnsiConsole.MarkupLine($"  GDB x64: [blue]{gdbX64}[/]");
            AnsiConsole.MarkupLine($"  GDB ARM64: [blue]{gdbArm64}[/]");
            AnsiConsole.WriteLine();
        }

        return 0;
    }

    private static string GetPlatformName()
    {
        if (RuntimeInformation.IsOSPlatform(SysOSPlatform.Windows))
        {
            return "windows";
        }

        if (RuntimeInformation.IsOSPlatform(SysOSPlatform.OSX))
        {
            return "macos";
        }

        if (RuntimeInformation.IsOSPlatform(SysOSPlatform.Linux))
        {
            return "linux";
        }

        return "unknown";
    }

    private static string GetDisplayBackend()
    {
        if (RuntimeInformation.IsOSPlatform(SysOSPlatform.OSX))
        {
            return "cocoa";
        }

        return "gtk";
    }

    private static string GetGdbCommand(string targetArch)
    {
        string cosmosTools = ToolChecker.GetCosmosToolsPath();
        string ext = RuntimeInformation.IsOSPlatform(SysOSPlatform.Windows) ? ".exe" : "";

        // Check cosmos tools for arch-specific GDB
        string? cosmosGdb = targetArch switch
        {
            "x64" => FindFile(Path.Combine(cosmosTools, "x86_64-elf-tools", "bin", $"x86_64-elf-gdb{ext}")),
            "arm64" => FindFile(Path.Combine(cosmosTools, "aarch64-elf-tools", "bin", $"gdb{ext}"))
                    ?? FindFile(Path.Combine(cosmosTools, "aarch64-elf-tools", "bin", $"aarch64-none-elf-gdb{ext}")),
            _ => null
        };
        if (cosmosGdb != null)
        {
            return cosmosGdb;
        }

        // Linux: gdb-multiarch works for all architectures
        if (RuntimeInformation.IsOSPlatform(SysOSPlatform.Linux) && File.Exists("/usr/bin/gdb-multiarch"))
        {
            return "gdb-multiarch";
        }

        // macOS: brew installs arch-specific GDB
        if (RuntimeInformation.IsOSPlatform(SysOSPlatform.OSX))
        {
            string? brewGdb = targetArch switch
            {
                "x64" => FindFile("/opt/homebrew/bin/x86_64-elf-gdb") ?? FindFile("/usr/local/bin/x86_64-elf-gdb"),
                "arm64" => FindFile("/opt/homebrew/bin/aarch64-elf-gdb") ?? FindFile("/usr/local/bin/aarch64-elf-gdb"),
                _ => null
            };
            if (brewGdb != null)
            {
                return brewGdb;
            }
        }

        return "gdb";
    }

    private static string? FindFile(string path) => File.Exists(path) ? path : null;

    private static string? GetCosmosToolsPath()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string toolsDir = Path.Combine(home, ".dotnet", "tools");

        if (RuntimeInformation.IsOSPlatform(SysOSPlatform.Windows))
        {
            string exePath = Path.Combine(toolsDir, "cosmos.exe");
            if (File.Exists(exePath))
            {
                return exePath;
            }
        }
        else
        {
            string path = Path.Combine(toolsDir, "cosmos");
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static string EscapeJson(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }
}
