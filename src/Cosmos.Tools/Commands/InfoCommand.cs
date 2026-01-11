using System.ComponentModel;
using System.Runtime.InteropServices;
using Spectre.Console;
using Spectre.Console.Cli;
using Cosmos.Tools.Platform;
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
        var platform = GetPlatformName();
        var arch = PlatformInfo.CurrentArch.ToString().ToLower();
        var packageManager = PlatformInfo.GetPackageManager();
        var displayBackend = GetDisplayBackend();
        var cosmosToolsPath = GetCosmosToolsPath();
        var gdbCommand = GetGdbCommand();
        var arm64UefiBios = GetArm64UefiBiosPath();

        if (settings.Json)
        {
            Console.WriteLine("{");
            Console.WriteLine($"  \"platform\": \"{platform}\",");
            Console.WriteLine($"  \"platformName\": \"{PlatformInfo.GetDistroName()}\",");
            Console.WriteLine($"  \"arch\": \"{arch}\",");
            Console.WriteLine($"  \"packageManager\": \"{packageManager}\",");
            Console.WriteLine($"  \"qemuDisplay\": \"{displayBackend}\",");
            Console.WriteLine($"  \"gdbCommand\": \"{gdbCommand}\",");
            Console.WriteLine($"  \"arm64UefiBios\": {(arm64UefiBios != null ? $"\"{EscapeJson(arm64UefiBios)}\"" : "null")},");
            Console.WriteLine($"  \"cosmosToolsPath\": {(cosmosToolsPath != null ? $"\"{EscapeJson(cosmosToolsPath)}\"" : "null")}");
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
            AnsiConsole.MarkupLine($"  GDB Command: [blue]{gdbCommand}[/]");
            if (arm64UefiBios != null)
            {
                AnsiConsole.MarkupLine($"  ARM64 UEFI BIOS: [blue]{arm64UefiBios}[/]");
            }
            if (cosmosToolsPath != null)
            {
                AnsiConsole.MarkupLine($"  Cosmos Tools: [blue]{cosmosToolsPath}[/]");
            }
            AnsiConsole.WriteLine();
        }

        return 0;
    }

    private static string GetPlatformName()
    {
        if (RuntimeInformation.IsOSPlatform(SysOSPlatform.Windows))
            return "windows";
        if (RuntimeInformation.IsOSPlatform(SysOSPlatform.OSX))
            return "macos";
        if (RuntimeInformation.IsOSPlatform(SysOSPlatform.Linux))
            return "linux";
        return "unknown";
    }

    private static string GetDisplayBackend()
    {
        if (RuntimeInformation.IsOSPlatform(SysOSPlatform.Windows))
            return "sdl";
        if (RuntimeInformation.IsOSPlatform(SysOSPlatform.OSX))
            return "cocoa";
        return "gtk";
    }

    private static string GetGdbCommand()
    {
        // On Linux, prefer gdb-multiarch for cross-architecture debugging
        if (RuntimeInformation.IsOSPlatform(SysOSPlatform.Linux))
        {
            // Check if gdb-multiarch exists
            var gdbMultiarchPath = "/usr/bin/gdb-multiarch";
            if (File.Exists(gdbMultiarchPath))
                return "gdb-multiarch";
        }

        // macOS and Windows typically just use 'gdb'
        // Windows might need the full path if installed via MinGW
        if (RuntimeInformation.IsOSPlatform(SysOSPlatform.Windows))
        {
            var mingwPath = @"C:\msys64\mingw64\bin\gdb.exe";
            if (File.Exists(mingwPath))
                return mingwPath;
        }

        return "gdb";
    }

    private static string? GetCosmosToolsPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var toolsDir = Path.Combine(home, ".dotnet", "tools");

        if (RuntimeInformation.IsOSPlatform(SysOSPlatform.Windows))
        {
            var exePath = Path.Combine(toolsDir, "cosmos.exe");
            if (File.Exists(exePath)) return exePath;
        }
        else
        {
            var path = Path.Combine(toolsDir, "cosmos");
            if (File.Exists(path)) return path;
        }

        return null;
    }

    private static string? GetArm64UefiBiosPath()
    {
        var paths = new List<string>();

        // Linux paths
        paths.AddRange([
            "/usr/share/AAVMF/AAVMF_CODE.fd",
            "/usr/share/qemu-efi-aarch64/QEMU_EFI.fd",
            "/usr/share/edk2/aarch64/QEMU_EFI.fd",
            "/usr/share/edk2-aarch64/QEMU_EFI.fd"
        ]);

        // macOS Homebrew paths (both Intel and ARM)
        paths.AddRange([
            "/opt/homebrew/share/qemu/edk2-aarch64-code.fd",
            "/usr/local/share/qemu/edk2-aarch64-code.fd",
            "/opt/homebrew/opt/qemu/share/qemu/edk2-aarch64-code.fd",
            "/usr/local/opt/qemu/share/qemu/edk2-aarch64-code.fd"
        ]);

        // Windows paths
        if (RuntimeInformation.IsOSPlatform(SysOSPlatform.Windows))
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            paths.AddRange([
                Path.Combine(programFiles, "qemu", "share", "edk2-aarch64-code.fd"),
                Path.Combine(programFilesX86, "qemu", "share", "edk2-aarch64-code.fd"),
                @"C:\Program Files\qemu\share\edk2-aarch64-code.fd",
                @"C:\tools\qemu\share\edk2-aarch64-code.fd"
            ]);
        }

        return paths.FirstOrDefault(File.Exists);
    }

    private static string EscapeJson(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }
}
