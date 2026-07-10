namespace Cosmos.Tools.Platform;

public class ToolStatus
{
    public required ToolDefinition Tool { get; init; }
    public bool Found { get; init; }
    public string? Version { get; init; }
    public string? Path { get; init; }
    public string? FoundCommand { get; init; }
    /// <summary>How the tool was located (Override / System / Bundle / NotFound).</summary>
    public ToolSource Source { get; init; } = ToolSource.NotFound;
}

public static class ToolChecker
{
    public static async Task<ToolStatus> CheckToolAsync(ToolDefinition tool)
    {
        return tool switch
        {
            CommandToolDefinition cmd => await CheckCommandToolAsync(cmd),
            _ => new ToolStatus { Tool = tool, Found = false }
        };
    }

    private static async Task<ToolStatus> CheckCommandToolAsync(CommandToolDefinition tool)
    {
        // Delegate to ToolResolver so cosmos check, MSBuild, and the test runner all
        // agree on which binary to use for any given tool.
        ResolvedTool resolved = await ToolResolver.ResolveAsync(tool);
        if (resolved.Source != ToolSource.NotFound && File.Exists(resolved.Path))
        {
            return new ToolStatus
            {
                Tool = tool,
                Found = true,
                Version = resolved.Version,
                Path = resolved.Path,
                FoundCommand = Path.GetFileName(resolved.Path),
                Source = resolved.Source
            };
        }
        return new ToolStatus { Tool = tool, Found = false, Source = ToolSource.NotFound };
    }

    public static async Task<List<ToolStatus>> CheckAllToolsAsync()
    {
        var tools = ToolDefinitions.GetAllTools();
        var results = new List<ToolStatus>();

        foreach (var tool in tools)
        {
            var status = await CheckToolAsync(tool);
            results.Add(status);
        }

        return results;
    }

    public static string GetCosmosToolsPath()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return PlatformInfo.CurrentOS == OSPlatform.Windows
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cosmos", "Tools")
            : Path.Combine(home, ".cosmos", "tools");
    }
}
