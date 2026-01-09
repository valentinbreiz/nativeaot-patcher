using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Cosmos.Tools.Platform;

namespace Cosmos.Tools.Commands;

public class CheckSettings : CommandSettings
{
    [CommandOption("-a|--arch")]
    [Description("Target architecture (x64, arm64, or 'all' for both)")]
    public string? Arch { get; set; }

    [CommandOption("--json")]
    [Description("Output results as JSON")]
    public bool Json { get; set; }
}

public class CheckCommand : AsyncCommand<CheckSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, CheckSettings settings)
    {
        if (!settings.Json)
        {
            PrintHeader();
        }

        var results = await ToolChecker.CheckAllToolsAsync(settings.Arch);

        if (settings.Json)
        {
            PrintJsonResults(results);
        }
        else
        {
            PrintResults(results);
            PrintSummary(results);
        }

        return 0;
    }

    private static void PrintHeader()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [bold]Cosmos Tools Check[/]");
        AnsiConsole.WriteLine("  " + new string('-', 50));
        AnsiConsole.MarkupLine($"  Platform: [blue]{PlatformInfo.GetDistroName()}[/] ({PlatformInfo.CurrentArch})");
        AnsiConsole.MarkupLine($"  Package Manager: [blue]{PlatformInfo.GetPackageManager()}[/]");
        AnsiConsole.WriteLine("  " + new string('-', 50));
        AnsiConsole.WriteLine();
    }

    private static void PrintResults(List<ToolStatus> results)
    {
        var maxNameLen = results.Max(r => r.Tool.DisplayName.Length);

        foreach (var result in results)
        {
            var status = result.Found ? "[green]\u2713[/]" : "[red]\u2717[/]";
            var required = result.Tool.Required ? "" : " [dim](optional)[/]";
            var version = result.Version != null ? $" [dim]({result.Version})[/]" : "";

            var name = result.Tool.DisplayName.PadRight(maxNameLen);

            if (result.Found)
            {
                AnsiConsole.MarkupLine($"  {status} {name}{version}");
            }
            else
            {
                AnsiConsole.MarkupLine($"  {status} {name} [yellow]- Not found{required}[/]");
            }
        }
    }

    private static void PrintSummary(List<ToolStatus> results)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("  " + new string('-', 50));

        var requiredMissing = results.Where(r => r.Tool.Required && !r.Found).ToList();
        var optionalMissing = results.Where(r => !r.Tool.Required && !r.Found).ToList();
        var allFound = results.All(r => r.Found || !r.Tool.Required);

        if (allFound)
        {
            AnsiConsole.MarkupLine("  [green]All required tools are installed![/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"  [yellow]Missing {requiredMissing.Count} required tool(s)[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("  Run [blue]cosmos-tools install[/] to install missing tools");
        }

        if (optionalMissing.Count > 0)
        {
            AnsiConsole.MarkupLine($"  [dim]({optionalMissing.Count} optional tool(s) not installed)[/]");
        }

        AnsiConsole.WriteLine();
    }

    private static void PrintJsonResults(List<ToolStatus> results)
    {
        Console.WriteLine("{");
        Console.WriteLine($"  \"platform\": \"{PlatformInfo.CurrentOS}\",");
        Console.WriteLine($"  \"architecture\": \"{PlatformInfo.CurrentArch}\",");
        Console.WriteLine($"  \"packageManager\": \"{PlatformInfo.GetPackageManager()}\",");
        Console.WriteLine("  \"tools\": [");

        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            var comma = i < results.Count - 1 ? "," : "";
            Console.WriteLine("    {");
            Console.WriteLine($"      \"name\": \"{r.Tool.Name}\",");
            Console.WriteLine($"      \"displayName\": \"{r.Tool.DisplayName}\",");
            Console.WriteLine($"      \"found\": {r.Found.ToString().ToLower()},");
            Console.WriteLine($"      \"required\": {r.Tool.Required.ToString().ToLower()},");
            Console.WriteLine($"      \"version\": {(r.Version != null ? $"\"{r.Version}\"" : "null")},");
            Console.WriteLine($"      \"path\": {(r.Path != null ? $"\"{r.Path.Replace("\\", "\\\\")}\"" : "null")}");
            Console.WriteLine($"    }}{comma}");
        }

        Console.WriteLine("  ]");
        Console.WriteLine("}");
    }
}
