// This code is licensed under MIT license (see LICENSE for details)

using System.Security.Cryptography;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Cosmos.Build.Asm.Tasks;

public sealed class YasmBuildTask : ToolTask
{
    [Required] public string? YasmPath { get; set; }
    [Required] public string[]? SourceFiles { get; set; }
    [Required] public string? OutputPath { get; set; }

    protected override MessageImportance StandardErrorLoggingImportance => MessageImportance.Normal;

    protected override string GenerateFullPathToTool() =>
        YasmPath!;

    private string? FilePath { get; set; }
    private string? FileName { get; set; }

    protected override string GenerateCommandLineCommands()
    {
        Log.LogMessage(MessageImportance.Low, $"[Debug] Generating command-line args for {FilePath} -> {FileName}");
        StringBuilder sb = new();

        sb.Append($" -felf64 ");
        sb.Append($" -o {Path.Combine(OutputPath, FileName)} ");
        sb.Append($" {FilePath} ");

        return sb.ToString();
    }

    public override bool Execute()
    {
        LogStandardErrorAsError = true;
        Log.LogMessage(MessageImportance.High, "Running Cosmos.Asm-Yasm...");
        Log.LogMessage(MessageImportance.High, $"Tool Path: {YasmPath}");

        string paths = string.Join(",", SourceFiles);
        Log.LogMessage(MessageImportance.High, $"Source Files: {paths}");
        Log.LogMessage(MessageImportance.Low, "[Debug] Beginning file matching");

        if (!Directory.Exists(OutputPath))
        {
            Log.LogMessage(MessageImportance.Low, $"[Debug] Creating output directory: {OutputPath}");
            Directory.CreateDirectory(OutputPath);
        }

        using SHA1? hasher = SHA1.Create();

        foreach (string file in SourceFiles!)
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
