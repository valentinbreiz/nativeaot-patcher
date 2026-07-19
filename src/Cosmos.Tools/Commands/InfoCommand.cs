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

public class InfoCommand : AsyncCommand<InfoSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, InfoSettings settings)
    {
        string platform = GetPlatformName();
        string arch = PlatformInfo.CurrentArch.ToString().ToLower();
        string packageManager = PlatformInfo.GetPackageManager();

        if (settings.Json)
        {
            Console.WriteLine("{");
            Console.WriteLine($"  \"platform\": \"{platform}\",");
            Console.WriteLine($"  \"platformName\": \"{PlatformInfo.GetDistroName()}\",");
            Console.WriteLine($"  \"arch\": \"{arch}\",");
            Console.WriteLine($"  \"packageManager\": \"{packageManager}\"");
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
            AnsiConsole.WriteLine();
        }

        return 0;
    }

    private static async Task<string?> ResolveToolPathAsync(CommandToolDefinition tool)
    {
        var status = await ToolChecker.CheckToolAsync(tool);
        return status.Found ? status.Path : null;
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

    private static string EscapeJson(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }
}
