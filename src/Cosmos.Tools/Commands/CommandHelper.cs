using Cosmos.Tools.Platform;
using Spectre.Console;

namespace Cosmos.Tools.Commands;

public static class CommandHelper
{
    public static void PrintHeader(string title, string? mode = null)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"  [bold]{title}[/]");
        AnsiConsole.WriteLine("  " + new string('-', 50));
        AnsiConsole.MarkupLine($"  Platform: [blue]{PlatformInfo.GetDistroName()}[/] ({PlatformInfo.CurrentArch})");
        AnsiConsole.MarkupLine($"  Package Manager: [blue]{PlatformInfo.GetPackageManager()}[/]");
        if (mode != null)
        {
            AnsiConsole.MarkupLine($"  Mode: [blue]{mode}[/]");
        }

        AnsiConsole.WriteLine("  " + new string('-', 50));
        AnsiConsole.WriteLine();
    }
}
