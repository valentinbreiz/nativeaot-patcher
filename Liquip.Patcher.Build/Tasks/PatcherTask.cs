using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Liquip.Patcher.Build.Tasks;

public class PatcherTask : ToolTask
{
    
    [Required]
    public string PatcherPath { get; set; } = null!;

    [Required]
    public string TargetAssembly { get; set; } = null!;

    [Required]
    public ITaskItem[] References { get; set; } = null!;
    
    [Required]
    public ITaskItem[] PlugsReferences { get; set; } = null!;
    
    protected override string GenerateFullPathToTool()
    {
        return PatcherPath;
    }

    public override bool Execute()
    {
        return base.Execute();
    }

    protected override string ToolName { get; } = nameof(PatcherTask);

    protected override string GenerateResponseFileCommands()
    {
        var args = new Dictionary<string, string>
        {
            [nameof(TargetAssembly)] = TargetAssembly,
        }.ToList();
        
        args.AddRange(References
            .Select(reference =>
                new KeyValuePair<string, string>(nameof(References), reference.ItemSpec)
            )
        );

        args.AddRange(PlugsReferences
            .Select(plugsReference =>
                new KeyValuePair<string, string>(nameof(PlugsReferences), plugsReference.ItemSpec)
            )
        );

        return string.Join(Environment.NewLine, args.Select(a => $"{a.Key}:{a.Value}"));
    }
}