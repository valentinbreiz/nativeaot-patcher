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
    [Required] public string[] SearchPath { get; set; }
    [Required] public string OutputPath { get; set; }

    protected override string GenerateFullPathToTool() =>
        YasmPath;

    private string FilePath { get; set; }
    private string FileName { get; set; }


    protected override string GenerateCommandLineCommands()
    {
        StringBuilder sb = new();

        sb.Append($" -a amd64 ");
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

        List<string> files = new();

        Matcher matcher = new();
        matcher.AddIncludePatterns(["*.s", "**.s"]);

        foreach (string path in SearchPath)
        {
            PatternMatchingResult result = matcher.Execute(
                new DirectoryInfoWrapper(
                    new DirectoryInfo(path + "x86_64/")));
            files.AddRange(result.Files.Select(i => i.Path));
        }

        using SHA1? hasher = SHA1.Create();

        foreach (string? file in files)
        {
            FilePath = file;
            using FileStream stream = File.OpenRead(FilePath);
            byte[] fileHash = hasher.ComputeHash(stream);
            FileName = $"{Path.GetFileName(file)}-{Convert.ToBase64String(fileHash)}.obj";
            if (!base.Execute())
            {
                return false;
            }
        }

        return true;
    }

    protected override string ToolName => "Cosmos.Asm-Yasm";
}
