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
    private const int CommandTimeoutMs = 5000;

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

            string candidatePath = whichResult.output.Split('\n', '\r')[0].Trim();
            if (string.IsNullOrWhiteSpace(candidatePath) || !File.Exists(candidatePath))
            {
                // Check Cosmos tools paths — installers place tools in subdirectories:
                //   {tools}/yasm/yasm, {tools}/lld/ld.lld,
                //   {tools}/x86_64-elf-tools/bin/x86_64-elf-gcc, etc.
                //   {tools}/bin/ contains symlinks (Linux/macOS)
                string cosmosToolsPath = GetCosmosToolsPath();
                string ext = PlatformInfo.CurrentOS == OSPlatform.Windows ? ".exe" : "";

                var possiblePaths = new List<string>();

                // Flat layout & bin/ symlinks
                AddPathVariants(possiblePaths, cosmosToolsPath, command, ext);
                AddPathVariants(possiblePaths, Path.Combine(cosmosToolsPath, "bin"), command, ext);

                // Tool in its own subdirectory (yasm/yasm, lld/ld.lld, xorriso/xorriso)
                AddPathVariants(possiblePaths, Path.Combine(cosmosToolsPath, command), command, ext);

                // Cross-compiler toolchains have a bin/ subdirectory
                AddPathVariants(possiblePaths, Path.Combine(cosmosToolsPath, "x86_64-elf-tools", "bin"), command, ext);
                AddPathVariants(possiblePaths, Path.Combine(cosmosToolsPath, "aarch64-elf-tools", "bin"), command, ext);

                // QEMU
                AddPathVariants(possiblePaths, Path.Combine(cosmosToolsPath, "qemu"), command, ext);

                // Named subdirectories for specific tools
                foreach (string dir in new[] { "lld", "xorriso", "yasm" })
                {
                    AddPathVariants(possiblePaths, Path.Combine(cosmosToolsPath, dir), command, ext);
                }

                foreach (string possiblePath in possiblePaths)
                {
                    if (File.Exists(possiblePath))
                    {
                        string? version = await GetVersionAsync(possiblePath, versionArg);
                        return (true, version, possiblePath);
                    }
                }

                return (false, null, null);
            }

            string? version2 = null;
            if (!string.IsNullOrEmpty(versionArg))
                version2 = await GetVersionAsync(candidatePath, versionArg);

            return (true, version2, candidatePath);
        }
        catch
        {
            return (false, null, null);
        }
    }

    private static void AddPathVariants(List<string> paths, string dir, string command, string ext)
    {
        if (!string.IsNullOrEmpty(ext))
        {
            paths.Add(Path.Combine(dir, command + ext));
        }
        paths.Add(Path.Combine(dir, command));
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
                // Try each non-empty line for a version pattern
                foreach (string line in result.output.Split('\n', '\r'))
                {
                    string trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed))
                    {
                        continue;
                    }
                    var versionMatch = System.Text.RegularExpressions.Regex.Match(trimmed, @"(\d+\.[\d.]+)");
                    if (versionMatch.Success)
                    {
                        return versionMatch.Value;
                    }
                }
                // No version pattern found — return first non-empty line
                string firstLine = result.output.Split('\n', '\r').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? "";
                return string.IsNullOrEmpty(firstLine) ? null : firstLine;
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
                Path.Combine(toolsBase, "x86_64-elf-tools", "bin"),
                Path.Combine(toolsBase, "aarch64-elf-tools", "bin"),
                Path.Combine(toolsBase, "qemu"));
            string currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            psi.Environment["PATH"] = $"{extraPaths}{sep}{currentPath}";

            using var process = Process.Start(psi);
            if (process == null)
            {
                return (false, "");
            }

            // Read stdout and stderr concurrently to avoid deadlocks
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            // Apply timeout to prevent hanging on tools that wait for input
            using var cts = new CancellationTokenSource(CommandTimeoutMs);
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(); } catch { }
                return (false, "");
            }

            string output = await stdoutTask;
            string error = await stderrTask;

            // Combine stdout + stderr so version info is found regardless of which stream it's on
            string combined = (output + "\n" + error).Trim();
            return (process.ExitCode == 0 || !string.IsNullOrEmpty(combined), combined);
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
