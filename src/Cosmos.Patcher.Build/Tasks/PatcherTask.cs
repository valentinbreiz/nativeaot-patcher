using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Cosmos.Patcher.Build.Tasks;

public sealed class PatcherTask : ToolTask
{
    [Required] public string? PatcherPath { get; set; }

    [Required] public string? TargetAssembly { get; set; }

    [Required] public required ITaskItem[] PlugsReferences { get; set; }

    [Required] public required string OutputPath { get; set; }

    protected override string GenerateFullPathToTool() =>
        // Return Liquip.Patcher.exe path
        PatcherPath;

    protected override string GenerateCommandLineCommands()
    {
        CommandLineBuilder builder = new();

        // Add main command
        builder.AppendSwitch("patch");

        // Add --target arg
        builder.AppendSwitch("--target");
        builder.AppendFileNameIfNotNull(TargetAssembly);


        // Add plugs
        builder.AppendSwitch("--plugs");
        foreach (ITaskItem plug in PlugsReferences)
        {
            builder.AppendFileNameIfNotNull(plug.ItemSpec);
        }

        // Add --output arg
        builder.AppendSwitch("--output");
        builder.AppendFileNameIfNotNull(OutputPath);

        return builder.ToString();
    }

    public override bool Execute()
    {
        Log.LogMessage(MessageImportance.High, "Running Liquip.Patcher...");
        Log.LogMessage(MessageImportance.High, $"Tool Path: {PatcherPath}");
        Log.LogMessage(MessageImportance.High, $"Target Assembly: {TargetAssembly}");
        Log.LogMessage(MessageImportance.High, $"Output Path: {OutputPath}");
        Log.LogMessage(MessageImportance.High,
            $"Plugs References: {string.Join(", ", PlugsReferences.Select(p => p.ItemSpec))}");

        return base.Execute();
    }

    protected override string ToolName => "Liquip.Patcher.exe";
}
