using System.Diagnostics;

namespace Cosmos.Tools.Platform;

public class ToolStatus
{
    public required ToolDefinition Tool { get; init; }
    public bool Found { get; init; }
    public string? Version { get; init; }
    public string? Path { get; init; }
    public string? FoundCommand { get; init; }
}

public static class ToolChecker
{
    public static async Task<ToolStatus> CheckToolAsync(ToolDefinition tool)
    {
        foreach (string command in tool.Commands)
        {
            var (found, version, path) = await TryFindCommandAsync(command, tool.VersionArg);
            if (found)
            {
                return new ToolStatus
                {
                    Tool = tool,
                    Found = true,
                    Version = version,
                    Path = path,
                    FoundCommand = command
                };
            }
        }

        return new ToolStatus
        {
            Tool = tool,
            Found = false
        };
    }

    public static async Task<List<ToolStatus>> CheckAllToolsAsync(string? architecture = null)
    {
        var tools = ToolDefinitions.GetToolsForArchitecture(architecture);
        var results = new List<ToolStatus>();

        foreach (var tool in tools)
        {
            var status = await CheckToolAsync(tool);
            results.Add(status);
        }

        return results;
    }

    private static async Task<(bool found, string? version, string? path)> TryFindCommandAsync(string command, string? versionArg)
    {
        try
        {
            // First try to find the command using 'which' (Unix) or 'where' (Windows)
            string whichCommand = PlatformInfo.CurrentOS == OSPlatform.Windows ? "where" : "which";
            var whichResult = await RunCommandAsync(whichCommand, command);

            if (!whichResult.success || string.IsNullOrWhiteSpace(whichResult.output))
            {
                // Also check common Cosmos tools paths
                string cosmosToolsPath = GetCosmosToolsPath();
                string[] possiblePaths = new[]
                {
                    Path.Combine(cosmosToolsPath, command),
                    Path.Combine(cosmosToolsPath, command + ".exe"),
                    Path.Combine(cosmosToolsPath, "bin", command),
                    Path.Combine(cosmosToolsPath, "bin", command + ".exe")
                };

                foreach (string? possiblePath in possiblePaths)
                {
                    if (File.Exists(possiblePath))
                    {
                        string? version = await GetVersionAsync(possiblePath, versionArg);
                        return (true, version, possiblePath);
                    }
                }

                return (false, null, null);
            }

            string path = whichResult.output.Split('\n', '\r')[0].Trim();

            // Get version if possible
            string? version2 = null;
            if (!string.IsNullOrEmpty(versionArg))
            {
                version2 = await GetVersionAsync(command, versionArg);
            }

            return (true, version2, path);
        }
        catch
        {
            return (false, null, null);
        }
    }

    private static async Task<string?> GetVersionAsync(string command, string? versionArg)
    {
        if (string.IsNullOrEmpty(versionArg))
            return null;

        try
        {
            var result = await RunCommandAsync(command, versionArg);
            if (result.success && !string.IsNullOrWhiteSpace(result.output))
            {
                // Extract version from first line
                string firstLine = result.output.Split('\n', '\r')[0].Trim();
                // Try to find version pattern
                var versionMatch = System.Text.RegularExpressions.Regex.Match(firstLine, @"(\d+\.[\d.]+)");
                return versionMatch.Success ? versionMatch.Value : firstLine;
            }
        }
        catch { }

        return null;
    }

    private static async Task<(bool success, string output)> RunCommandAsync(string command, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
                return (false, "");

            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            // Some tools output version to stderr
            string combinedOutput = string.IsNullOrEmpty(output) ? error : output;
            return (process.ExitCode == 0 || !string.IsNullOrEmpty(combinedOutput), combinedOutput);
        }
        catch
        {
            return (false, "");
        }
    }

    public static string GetCosmosToolsPath()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return PlatformInfo.CurrentOS == OSPlatform.Windows
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cosmos", "tools")
            : Path.Combine(home, ".cosmos", "tools");
    }
}
