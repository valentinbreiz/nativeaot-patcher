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
                // Check Cosmos tools paths — installers place tools in subdirectories:
                //   {tools}/yasm/yasm, {tools}/lld/ld.lld,
                //   {tools}/x86_64-elf-tools/bin/x86_64-elf-gcc, etc.
                //   {tools}/bin/ contains symlinks (Linux/macOS)
                string cosmosToolsPath = GetCosmosToolsPath();
                string ext = PlatformInfo.CurrentOS == OSPlatform.Windows ? ".exe" : "";
                var possiblePaths = new List<string>
                {
                    // Flat layout & bin/ symlinks
                    Path.Combine(cosmosToolsPath, command + ext),
                    Path.Combine(cosmosToolsPath, command),
                    Path.Combine(cosmosToolsPath, "bin", command + ext),
                    Path.Combine(cosmosToolsPath, "bin", command),
                    // Tool in its own subdirectory (yasm/yasm, lld/ld.lld, xorriso/xorriso)
                    Path.Combine(cosmosToolsPath, command, command + ext),
                    Path.Combine(cosmosToolsPath, command, command),
                    // Cross-compiler toolchains have a bin/ subdirectory
                    Path.Combine(cosmosToolsPath, "x86_64-elf-tools", "bin", command + ext),
                    Path.Combine(cosmosToolsPath, "x86_64-elf-tools", "bin", command),
                    Path.Combine(cosmosToolsPath, "aarch64-elf-tools", "bin", command + ext),
                    Path.Combine(cosmosToolsPath, "aarch64-elf-tools", "bin", command),
                    // Named subdirectories for specific tools
                    Path.Combine(cosmosToolsPath, "lld", command + ext),
                    Path.Combine(cosmosToolsPath, "lld", command),
                    Path.Combine(cosmosToolsPath, "xorriso", command + ext),
                    Path.Combine(cosmosToolsPath, "xorriso", command),
                    Path.Combine(cosmosToolsPath, "yasm", command + ext),
                    Path.Combine(cosmosToolsPath, "yasm", command),
                    Path.Combine(cosmosToolsPath, "clang", command + ext),
                    Path.Combine(cosmosToolsPath, "clang", command),
                    Path.Combine(cosmosToolsPath, "make", command + ext),
                    Path.Combine(cosmosToolsPath, "make", command),
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

            // Get version using the resolved full path (not the bare command name)
            // so it works even when the command name has dots (e.g. ld.lld) or
            // isn't directly resolvable by the child process
            string? version2 = null;
            if (!string.IsNullOrEmpty(versionArg))
            {
                version2 = await GetVersionAsync(path, versionArg);
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
        {
            return null;
        }

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

            // Augment PATH with Cosmos installer tool directories so tools are
            // discoverable even if the shell hasn't picked up the PATH yet
            // (e.g. VS Code launched before install, or new terminal not opened)
            string toolsBase = GetCosmosToolsPath();
            string sep = PlatformInfo.CurrentOS == OSPlatform.Windows ? ";" : ":";
            string extraPaths = string.Join(sep,
                Path.Combine(toolsBase, "bin"),
                Path.Combine(toolsBase, "yasm"),
                Path.Combine(toolsBase, "xorriso"),
                Path.Combine(toolsBase, "lld"),
                Path.Combine(toolsBase, "clang"),
                Path.Combine(toolsBase, "make"),
                Path.Combine(toolsBase, "x86_64-elf-tools", "bin"),
                Path.Combine(toolsBase, "aarch64-elf-tools", "bin"));
            string currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            psi.Environment["PATH"] = $"{extraPaths}{sep}{currentPath}";

            using var process = Process.Start(psi);
            if (process == null)
            {
                return (false, "");
            }

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
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cosmos", "Tools")
            : Path.Combine(home, ".cosmos", "tools");
    }

}
