using System.ComponentModel;
using Cosmos.Tools.Platform;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Cosmos.Tools.Commands;

public class CheckSettings : CommandSettings
{
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
            CommandHelper.PrintHeader("Cosmos Tools Check");
        }

        var results = await ToolChecker.CheckAllToolsAsync();

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

    private static void PrintResults(List<ToolStatus> results)
    {
        int maxNameLen = results.Max(r => r.Tool.DisplayName.Length);

        foreach (var result in results)
        {
            bool detected = result.Found && (result.Version != null || result.Tool is FileToolDefinition);
            string status = detected ? "[green]\u2713[/]" : "[red]\u2717[/]";
            string required = result.Tool.Required ? "" : " [dim](optional)[/]";
            string name = result.Tool.DisplayName.PadRight(maxNameLen);

            if (detected)
            {
                string ver = result.Version != null ? $" [dim]({result.Version})[/]" : "";
                string path = result.Path != null ? $" [dim]{Markup.Escape(result.Path)}[/]" : "";
                AnsiConsole.MarkupLine($"  {status} {name}{ver}{path}");
            }
            else if (result.Found)
            {
                string path = result.Path != null ? $" [dim]{Markup.Escape(result.Path)}[/]" : "";
                AnsiConsole.MarkupLine($"  {status} {name} [yellow]- Not detected{required}[/]{path}");
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

        bool IsDetected(ToolStatus r) => r.Found && (r.Version != null || r.Tool is FileToolDefinition);
        var requiredMissing = results.Where(r => r.Tool.Required && !IsDetected(r)).ToList();
        var optionalMissing = results.Where(r => !r.Tool.Required && !IsDetected(r)).ToList();
        bool allFound = results.All(r => IsDetected(r) || !r.Tool.Required);

        if (allFound)
        {
            AnsiConsole.MarkupLine("  [green]All required tools are installed![/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"  [yellow]Missing {requiredMissing.Count} required tool(s)[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("  Run [blue]cosmos install[/] to install missing tools");
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
            string comma = i < results.Count - 1 ? "," : "";
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
