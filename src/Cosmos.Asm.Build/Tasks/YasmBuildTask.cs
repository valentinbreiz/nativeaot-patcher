// This code is licensed under MIT license (see LICENSE for details)

using System.Security.Cryptography;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace Cosmos.Asm.Build.Tasks;

public sealed class YasmBuildTask : ToolTask
{
    [Required] public string? YasmPath { get; set; }
    [Required] public string[]? SearchPath { get; set; }
    [Required] public string? OutputPath { get; set; }

    protected override string GenerateFullPathToTool() =>
        YasmPath;

    private string? FilePath { get; set; }
    private string? FileName { get; set; }

    protected override string GenerateCommandLineCommands()
    {
        Log.LogMessage(MessageImportance.Low, $"[Debug] Generating command-line args for {FilePath} -> {FileName}");
        StringBuilder sb = new();

        sb.Append(" -felf64 ");
        sb.Append($" -o {Path.Combine(OutputPath, FileName)} ");
        sb.Append($" {FilePath} ");

        return sb.ToString();
    }

    public override bool Execute()
    {
        Log.LogMessage(MessageImportance.High, "Running Cosmos.Asm-Yasm...");
        Log.LogMessage(MessageImportance.High, $"Tool Path: {YasmPath}");

        string paths = string.Join(",", SearchPath);
        Log.LogMessage(MessageImportance.High, $"Search Path: {paths}");
        Log.LogMessage(MessageImportance.Low, "[Debug] Beginning file matching");

        List<string> files = new();
        Matcher matcher = new();
        matcher.AddIncludePatterns(new[] { "*.asm", "**/*.asm" });

        // Search for .asm files in the provided search paths
        foreach (string asmFolder in SearchPath)
        {
            Log.LogMessage(MessageImportance.Low, $"[Debug] Searching in path: {asmFolder}");
            DirectoryInfo directory = new(asmFolder);
            if (!directory.Exists)
            {
                Log.LogWarning($"[Debug] Folder does not exist: {asmFolder}");
                continue;
            }

            PatternMatchingResult result = matcher.Execute(new DirectoryInfoWrapper(directory));
            files.AddRange(result.Files.Select(i => Path.Combine(asmFolder, i.Path)));
        }

        if (files.Count == 0)
        {
            Log.LogWarning("[Debug] No .asm files found to compile.");
            return true;
        }

        if (!Directory.Exists(OutputPath))
        {
            Log.LogMessage(MessageImportance.Low, $"[Debug] Creating output directory: {OutputPath}");
            Directory.CreateDirectory(OutputPath);
        }

        using SHA1? hasher = SHA1.Create();

        foreach (string? file in files)
        {
            FilePath = file;
            using FileStream stream = File.OpenRead(FilePath);
            byte[] fileHash = hasher.ComputeHash(stream);
            FileName = $"{Path.GetFileNameWithoutExtension(file)}-{BitConverter.ToString(fileHash).Replace("-", "").ToLower()}.obj";
            Log.LogMessage(MessageImportance.High, $"[Debug] About to run base.Execute() for {FileName}");

            if (!base.Execute())
            {
                Log.LogError($"[Debug] YasmBuildTask failed for {FilePath}");
                return false;
            }
        }

        Log.LogMessage(MessageImportance.High, "✅ YasmBuildTask completed successfully.");
        return true;
    }

    protected override string ToolName => "Cosmos.Asm-Yasm";
}