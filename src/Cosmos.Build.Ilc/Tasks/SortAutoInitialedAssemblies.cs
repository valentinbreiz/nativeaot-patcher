using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            SortedAssemblyNames = Array.Empty<ITaskItem>();
            return true;
        }

        try
        {
            var systemAssemblies = new HashSet<string>(
                AssemblyNames
                    .Select(a => a.ItemSpec)
                    .Where(n => n.StartsWith("System.", StringComparison.OrdinalIgnoreCase)),
                StringComparer.OrdinalIgnoreCase);

            var requestedAssemblies = new HashSet<string>(
                AssemblyNames.Select(a => a.ItemSpec),
                StringComparer.OrdinalIgnoreCase);

            var dllMap = AssemblyPaths.ToDictionary(
                item => Path.GetFileNameWithoutExtension(item.ItemSpec),
                item => item.ItemSpec,
                StringComparer.OrdinalIgnoreCase);

            var graph = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var allAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Build dependency graph only for assemblies we explicitly care about
            foreach (var kvp in dllMap)
            {
                var asmPath = kvp.Value;
                if (!File.Exists(asmPath))
                {
                    Log.LogWarning($"Assembly file not found: {asmPath}");
                    continue;
                }

                using var asm = AssemblyDefinition.ReadAssembly(asmPath);

                // Skip if this assembly isn't in AssemblyNames
                if (!requestedAssemblies.Contains(asm.Name.Name))
                    continue;

                allAssemblies.Add(kvp.Key);

                // Only consider dependencies that are also part of AssemblyNames
                var refs = new HashSet<string>(
                    asm.MainModule.AssemblyReferences
                        .Select(r => r.Name)
                        .Where(requestedAssemblies.Contains),
                    StringComparer.OrdinalIgnoreCase);

                graph[kvp.Key] = refs;
            }

            Log.LogMessage(MessageImportance.Low, "Performing topological sort on assembly dependency graph...");
            var sorted = TopologicalSort(graph, allAssemblies);

            // Non-System assemblies requested for sorting
            var nonSystemRequested = new HashSet<string>(
                requestedAssemblies.Where(a => !systemAssemblies.Contains(a)),
                StringComparer.OrdinalIgnoreCase);

            var sortedSet = new HashSet<string>(sorted, StringComparer.OrdinalIgnoreCase);
            var missing = nonSystemRequested.Where(name => !sortedSet.Contains(name));

            // System.* assemblies must always come first
            var ordered = systemAssemblies
                .Concat(sorted.Where(nonSystemRequested.Contains))
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
        var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in allNodes)
            inDegree[node] = 0;

        foreach (var deps in graph.Values)
        {
            foreach (var dep in deps)
            {
                if (inDegree.ContainsKey(dep))
                    inDegree[dep]++;
            }
        }

        var queue = new Queue<string>(
            inDegree.Where(kvp => kvp.Value == 0).Select(kvp => kvp.Key));

        var result = new List<string>();

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            result.Add(node);

            if (!graph.TryGetValue(node, out var deps)) continue;
            foreach (var dep in deps)
            {
                if (--inDegree[dep] == 0)
                    queue.Enqueue(dep);
            }
        }

        // Add any missing (e.g., in cycles)
        var missing = allNodes.Except(result, StringComparer.OrdinalIgnoreCase);
        result.AddRange(missing);

        return result;
    }
}
