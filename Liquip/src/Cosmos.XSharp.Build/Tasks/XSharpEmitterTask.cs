// This code is licensed under MIT license (see LICENSE for details)

using System.Text;
using Cosmos.API.Enum;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using XSharp.Build.Tasks;

namespace Cosmos.XSharp.Build.Tasks;

public sealed class XSharpEmitterTask : ToolTask
{
    [Required] public string? ObjectPath { get; set; }
    [Required] public string? OutputFile { get; set; }
    [Required] public string[]? Types { get; set; }
    [Required] public string? TargetPlatform { get; set; }

    protected override string GenerateFullPathToTool() =>
        ToolName;

    protected override string GenerateCommandLineCommands()
    {
        StringBuilder sb = new();

        sb.Append($" -o {OutputFile} ");
        sb.Append($" -platform {TargetPlatform} ");

        if (Types != null)
        {
            sb.Append($" -types {string.Join(",", Types)} ");
        }

        IEnumerable<string> paths = Directory.EnumerateFiles(ObjectPath, "*.obj", SearchOption.TopDirectoryOnly);
        sb.Append(string.Join(" ", paths));

        return sb.ToString();
    }

    public override bool Execute()
    {
        Log.LogMessage(MessageImportance.High, "Running Cosmos.XSharp-XSharpEmitter...");
        Log.LogMessage(MessageImportance.High, $"Object Path: {ObjectPath}");
        Log.LogMessage(MessageImportance.High, $"Output File: {OutputFile}");
        Log.LogMessage(MessageImportance.High, $"Target Platform: {TargetPlatform}");
        Log.LogMessage(MessageImportance.High, $"Types: {string.Join(", ", Types ?? Array.Empty<string>())}");

        try
        {
            var typeList = Types.Select(t => Type.GetType(t)).ToArray();
            var platform = Enum.Parse<TargetPlatform>(TargetPlatform);

            var xSharpString = XSharpEmitter.Emit(typeList, platform);

            File.WriteAllText(OutputFile, xSharpString);

            Log.LogMessage(MessageImportance.High, $"XSharp code emitted to: {OutputFile}");
            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex);
            return false;
        }
    }

    protected override string ToolName => "Cosmos.XSharp-XSharpEmitter";
}
