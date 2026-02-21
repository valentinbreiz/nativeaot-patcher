using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Mono.Cecil;

namespace Cosmos.Build.Ilc.Tasks;

#nullable disable
public class SortAutoInitialedAssemblies : Microsoft.Build.Utilities.Task
{
    [Required]
    public ITaskItem[] AssemblyPaths { get; set; }

    [Required]
    public ITaskItem[] AssemblyNames { get; set; }

    [Output]
    public ITaskItem[] SortedAssemblyNames { get; set; }

    public override bool Execute()
    {
        if (AssemblyNames.Length == 0)
        {
            Log.LogMessage(MessageImportance.High, "No assembly names provided, returning empty");
            SortedAssemblyNames = Array.Empty<ITaskItem>();
            return true;
        }

        try
        {
            HashSet<string> requestedAssemblies = new(
                AssemblyNames.Select(a => a.ItemSpec),
                StringComparer.OrdinalIgnoreCase);

            Dictionary<string, string> dllMap = AssemblyPaths.ToDictionary(
                item => Path.GetFileNameWithoutExtension(item.ItemSpec),
                item => item.ItemSpec,
                StringComparer.OrdinalIgnoreCase);

            Dictionary<string, HashSet<string>> graph = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> allAssemblies = new(StringComparer.OrdinalIgnoreCase);

            // Build dependency graph only for assemblies we explicitly care about
            foreach (KeyValuePair<string, string> kvp in dllMap)
            {
                string asmPath = kvp.Value;
                if (!File.Exists(asmPath))
                {
                    Log.LogWarning($"Assembly file not found: {asmPath}");
                    continue;
                }

                // Try to read the assembly, skip if it's not a valid IL assembly
                AssemblyDefinition asm;
                try
                {
                    asm = AssemblyDefinition.ReadAssembly(asmPath);
                    Log.LogMessage(MessageImportance.High, " Successfully loaded as IL assembly");
                }
                catch (BadImageFormatException ex)
                {
                    Log.LogMessage(MessageImportance.High,
                        "SKIPPED: Not a valid IL assembly (likely native/AOT compiled)");
                    Log.LogMessage(MessageImportance.Low, $"     Exception: {ex.Message}");
                    continue;
                }
                catch (Exception ex)
                {
                    Log.LogWarning($"Could not read assembly {asmPath}: {ex.GetType().Name} - {ex.Message}");
                    continue;
                }

                using (asm)
                {
                    // Skip if this assembly isn't in AssemblyNames
                    if (!requestedAssemblies.Contains(asm.Name.Name))
                        continue;

                    allAssemblies.Add(kvp.Key);

                    // Only consider dependencies that are also part of AssemblyNames
                    HashSet<string> refs = new(
                        asm.MainModule.AssemblyReferences
                            .Select(r => r.Name)
                            .Where(requestedAssemblies.Contains),
                        StringComparer.OrdinalIgnoreCase);
                    graph[kvp.Key] = refs;
                }
            }

            Log.LogMessage(MessageImportance.Low, "Performing topological sort on assembly dependency graph...");
            List<string> sorted = TopologicalSort(graph, allAssemblies);

            List<string> missing = requestedAssemblies
                .Where(name => !sorted.Contains(name, StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (missing.Count > 0)
            {
                Log.LogMessage(MessageImportance.High, "--- Missing assemblies (not in graph) ---");
                foreach (string m in missing)
                {
                    Log.LogMessage(MessageImportance.High, $"  - {m}");
                }
            }

            // System.* and sorted assemblies
            TaskItem[] ordered = sorted
                .Where(requestedAssemblies.Contains)
                .Concat(missing)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(n => new TaskItem(n))
                .ToArray();

            SortedAssemblyNames = ordered;
            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex);
            return false;
        }
    }

    private static List<string> TopologicalSort(
        Dictionary<string, HashSet<string>> graph,
        HashSet<string> allNodes)
    {
        Dictionary<string, int> inDegree = new(StringComparer.OrdinalIgnoreCase);
        foreach (string node in allNodes)
            inDegree[node] = 0;

        foreach (KeyValuePair<string, HashSet<string>> kvp in graph)
        {
            string node = kvp.Key;
            HashSet<string> deps = kvp.Value;
            // If node depends on deps, then node's in-degree increases
            inDegree[node] = deps.Count(dep => inDegree.ContainsKey(dep));
        }

        Queue<string> queue = new(
            inDegree.Where(kvp => kvp.Value == 0).Select(kvp => kvp.Key));

        List<string> result = new();

        while (queue.Count > 0)
        {
            string node = queue.Dequeue();
            result.Add(node);

            // Find all nodes that depend on this node
            foreach (KeyValuePair<string, HashSet<string>> kvp in graph)
            {
                string dependent = kvp.Key;
                HashSet<string> deps = kvp.Value;
                if (deps.Contains(node))
                {
                    if (--inDegree[dependent] == 0)
                        queue.Enqueue(dependent);
                }
            }
        }

        // Add any missing (e.g., in cycles)
        IEnumerable<string> missing = allNodes.Except(result, StringComparer.OrdinalIgnoreCase);
        result.AddRange(missing);

        return result;
    }
}
