// This code is licensed under MIT license (see LICENSE for details)

using System.Security.Cryptography;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace Cosmos.XSharp.Build.Tasks;

public sealed class XSharpEmitterTask : ToolTask
{
    [Required] public string? LdPath { get; set; }
    [Required] public string? ObjectPath { get; set; }
    [Required] public string? OutputFile { get; set; }

    protected override string GenerateFullPathToTool() =>
        LdPath;

    protected override string GenerateCommandLineCommands()
    {
        StringBuilder sb = new();

        sb.Append($" -o {OutputFile} ");

        IEnumerable<string> paths = Directory.EnumerateFiles(ObjectPath, "*.obj", SearchOption.TopDirectoryOnly);

        sb.Append(string.Join(" ", paths));

        return sb.ToString();
    }

    public override bool Execute()
    {
        Log.LogMessage(MessageImportance.High, "Running Cosmos.XSharp-XSharp...");
        Log.LogMessage(MessageImportance.High, $"Tool Path: {LdPath}");
        Log.LogMessage(MessageImportance.High, $"Object Path: {ObjectPath}");

        return base.Execute();
    }

    protected override string ToolName => "Cosmos.XSharp-XSharp";
}
