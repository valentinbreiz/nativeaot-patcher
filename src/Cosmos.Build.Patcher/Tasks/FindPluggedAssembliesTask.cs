using System.Linq;
using Cosmos.Patcher;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Cosmos.Build.Patcher.Tasks;

public sealed class FindPluggedAssembliesTask : Microsoft.Build.Utilities.Task
{
    [Required]
    public ITaskItem[] PlugAssemblies { get; set; } = [];

    [Required]
    public ITaskItem[] CandidateAssemblies { get; set; } = [];

    [Output]
    public ITaskItem[] AssembliesToPatch { get; set; } = [];

    public override bool Execute()
    {
        try
        {
            PlugScanner scanner = new();
            string[] plugs = PlugAssemblies.Select(p => p.ItemSpec).ToArray();
            string[] candidates = CandidateAssemblies.Select(c => c.ItemSpec).ToArray();
            AssembliesToPatch = scanner
                .FindPluggedAssemblies(plugs, candidates)
                .Select(p => new TaskItem(p))
                .ToArray();

            foreach (ITaskItem item in AssembliesToPatch)
                Log.LogMessage(MessageImportance.Low, $"Will patch: {item.ItemSpec}");

            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, true);
            return false;
        }
    }
}
