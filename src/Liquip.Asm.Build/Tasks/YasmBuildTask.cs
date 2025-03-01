// This code is licensed under MIT license (see LICENSE for details)

using System.Security.Cryptography;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace Liquip.Asm.Build.Tasks;

public class YasmBuildTask : ToolTask
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

        var sb = new StringBuilder();

        sb.Append($" -a amd64 ");
        sb.Append($" -o {Path.Combine(OutputPath, FileName)} ");
        sb.Append($" {FilePath} ");

        return sb.ToString();
    }

    public override bool Execute()
    {
        Log.LogMessage(MessageImportance.High, "Running Liquip.Asm-Yasm...");
        Log.LogMessage(MessageImportance.High, $"Tool Path: {YasmPath}");
        var paths = string.Join(",", SearchPath);
        Log.LogMessage(MessageImportance.High, $"Search Path: {paths}");

        var files = new List<string>();

        Matcher matcher = new();
        matcher.AddIncludePatterns(["*.s", "**.s"]);

        foreach (string path in SearchPath)
        {
            var result = matcher.Execute(
                new DirectoryInfoWrapper(
                    new DirectoryInfo(path + "x86_64/")));
            files.AddRange(result.Files.Select(i=>i.Path));
        }

        using var hasher = SHA1.Create();

        foreach (var file in files)
        {
            FilePath = file;
            using var stream = File.OpenRead(FilePath);
            var fileHash = hasher.ComputeHash(stream);
            FileName = $"{Path.GetFileName(file)}-{Convert.ToBase64String(fileHash)}.obj";
            if (!base.Execute())
            {
                return false;
            }
        }

        return true;
    }

    protected override string ToolName => "Liquip.Asm-Yasm";
}
