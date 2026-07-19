using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Cosmos.Patcher.Coverage;

/// <summary>
/// Instruments managed assemblies with method-level coverage tracking.
/// For each eligible method, inserts a CoverageTracker.Hit(id) call at method entry.
///
/// Design: uses a WHITELIST approach — only types whose namespace starts with the
/// include prefix (default: "Cosmos.Kernel") are eligible.  This automatically
/// excludes ILC-injected Internal.*, System.*, and other runtime support types
/// that live inside Cosmos assemblies but must not be instrumented.
/// </summary>
public class CoverageInstrumenter
{
    private readonly string _assemblyDir;
    private readonly string _outputMapPath;
    private readonly string _includePrefix;

    private int _nextMethodId;
    private readonly List<CoverageMapEntry> _map = [];
    /// <summary>
    /// Entries for plug-targeted methods in non-Cosmos assemblies. These are instrumented
    /// so their IDs can be shared with plug aliases, but are NOT written to coverage-map.txt
    /// (they would pollute the report with System.* projects).
    /// </summary>
    private readonly List<CoverageMapEntry> _forcedTargetMap = [];

    /// <summary>
    /// Assembly name prefixes to skip entirely (test infrastructure, framework, etc.).
    /// </summary>
    private static readonly string[] ExcludeAssemblies =
    [
        "Cosmos.TestRunner",
    ];

    /// <summary>
    /// Fully-qualified type names to always skip (avoids infinite recursion).
    /// </summary>
    private static readonly string[] ExcludeTypes =
    [
        "Cosmos.TestRunner.Framework.CoverageTracker",
    ];

    public CoverageInstrumenter(string assemblyDir, string outputMapPath, string includePrefix = "Cosmos.Kernel")
    {
        _assemblyDir = assemblyDir;
        _outputMapPath = outputMapPath;
        _includePrefix = includePrefix;
    }

    public int Instrument()
    {
        // Find the tracker assembly to import CoverageTracker.Hit method reference
        var trackerAssemblyPath = FindTrackerAssembly();
        if (trackerAssemblyPath == null)
        {
            Console.WriteLine("[Coverage] Warning: Cosmos.TestRunner.Framework.dll not found, skipping instrumentation.");
            return 0;
        }

        var trackerAssembly = AssemblyDefinition.ReadAssembly(trackerAssemblyPath);
        var hitMethodDef = FindHitMethod(trackerAssembly);
        if (hitMethodDef == null)
        {
            Console.WriteLine("[Coverage] Warning: CoverageTracker.Hit method not found, skipping instrumentation.");
            return 0;
        }

        // Parse plug-map files BEFORE instrumentation so we know which non-Cosmos
        // assemblies contain plug-targeted methods that need forced instrumentation.
        var plugMappings = ParsePlugMaps();
        var plugTargetsByAssembly = BuildPlugTargetIndex(plugMappings);

        // Process eligible assemblies in cosmos/ root and cosmos/ref/
        var dllFiles = new List<string>(Directory.GetFiles(_assemblyDir, "*.dll"));
        var refDir = Path.Combine(_assemblyDir, "ref");
        if (Directory.Exists(refDir))
        {
            dllFiles.AddRange(Directory.GetFiles(refDir, "*.dll"));
        }

        int instrumentedAssemblies = 0;

        foreach (var dllPath in dllFiles)
        {
            var assemblyName = Path.GetFileNameWithoutExtension(dllPath);

            if (ShouldInstrumentAssembly(assemblyName))
            {
                // Full instrumentation for Cosmos.Kernel.* assemblies
                try
                {
                    int count = InstrumentAssembly(dllPath, hitMethodDef);
                    if (count > 0)
                    {
                        instrumentedAssemblies++;
                        Console.WriteLine($"[Coverage] Instrumented {count} methods in {assemblyName}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Coverage] Warning: Failed to instrument {assemblyName}: {ex.Message}");
                }
            }
            else if (plugTargetsByAssembly.TryGetValue(assemblyName, out var forcedMethods))
            {
                // Selective instrumentation: only plug-targeted methods in non-Cosmos assemblies
                try
                {
                    int count = InstrumentAssembly(dllPath, hitMethodDef, forcedMethods);
                    if (count > 0)
                    {
                        instrumentedAssemblies++;
                        Console.WriteLine($"[Coverage] Instrumented {count} plug-targeted methods in {assemblyName}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Coverage] Warning: Failed to instrument plug targets in {assemblyName}: {ex.Message}");
                }
            }
        }

        // Add plug method aliases: map plug methods to the same ID as their patched targets
        int plugAliases = AddPlugAliases(plugMappings);

        // Write coverage map
        WriteCoverageMap();

        Console.WriteLine($"[Coverage] Total: {_nextMethodId} methods instrumented across {instrumentedAssemblies} assemblies");
        if (plugAliases > 0)
        {
            Console.WriteLine($"[Coverage] Added {plugAliases} plug method aliases from plug-map files");
        }

        return _nextMethodId;
    }

    /// <summary>
    /// Reads plug-map-*.txt files (produced by the patcher, one per assembly) and returns
    /// the raw list of plug mappings.
    /// </summary>
    private List<PlugMapEntry> ParsePlugMaps()
    {
        var plugMapFiles = new List<string>();
        plugMapFiles.AddRange(Directory.GetFiles(_assemblyDir, "plug-map-*.txt", SearchOption.TopDirectoryOnly));
        string refDir = Path.Combine(_assemblyDir, "ref");
        if (Directory.Exists(refDir))
        {
            plugMapFiles.AddRange(Directory.GetFiles(refDir, "plug-map-*.txt", SearchOption.TopDirectoryOnly));
        }

        var result = new List<PlugMapEntry>();
        foreach (var plugMapPath in plugMapFiles)
        {
            Console.WriteLine($"[Coverage] Reading plug map: {plugMapPath}");
            foreach (var line in File.ReadAllLines(plugMapPath))
            {
                if (line.StartsWith("#") || string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var parts = line.Split('\t');
                if (parts.Length < 6)
                {
                    continue;
                }

                result.Add(new PlugMapEntry
                {
                    PlugAssembly = parts[0],
                    PlugType = parts[1],
                    PlugMethod = parts[2],
                    TargetAssembly = parts[3],
                    TargetType = parts[4],
                    TargetMethod = parts[5],
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Builds a lookup of target assembly → set of (type, method) that need forced
    /// instrumentation because they are plug targets in non-Cosmos assemblies.
    /// </summary>
    private static Dictionary<string, HashSet<(string Type, string Method)>> BuildPlugTargetIndex(
        List<PlugMapEntry> mappings)
    {
        var result = new Dictionary<string, HashSet<(string, string)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in mappings)
        {
            if (!result.TryGetValue(m.TargetAssembly, out var set))
            {
                set = [];
                result[m.TargetAssembly] = set;
            }
            set.Add((m.TargetType, m.TargetMethod));
        }
        return result;
    }

    /// <summary>
    /// Adds alias entries in the coverage map for plug methods. When a target method was
    /// instrumented with a coverage ID, the corresponding plug method gets an entry with
    /// the SAME ID — so when the target is hit, the plug shows as covered too.
    /// </summary>
    private int AddPlugAliases(List<PlugMapEntry> mappings)
    {
        if (mappings.Count == 0)
        {
            return 0;
        }

        // Build lookup: "TargetAssembly\tTargetType\tTargetMethod" → coverage ID
        // Search both the main map and the forced-target map (hidden entries for
        // plug-targeted methods in non-Cosmos assemblies).
        var targetToId = new Dictionary<string, int>();
        foreach (var entry in _map)
        {
            string key = $"{entry.Assembly}\t{entry.Type}\t{entry.Method}";
            targetToId.TryAdd(key, entry.Id);
        }
        foreach (var entry in _forcedTargetMap)
        {
            string key = $"{entry.Assembly}\t{entry.Type}\t{entry.Method}";
            targetToId.TryAdd(key, entry.Id);
        }

        int count = 0;
        foreach (var m in mappings)
        {
            string targetKey = $"{m.TargetAssembly}\t{m.TargetType}\t{m.TargetMethod}";
            if (targetToId.TryGetValue(targetKey, out int targetId))
            {
                _map.Add(new CoverageMapEntry
                {
                    Id = targetId,       // Same ID as the target — shares the hit
                    Assembly = m.PlugAssembly,
                    Type = m.PlugType,
                    Method = m.PlugMethod,
                });
                count++;
            }
            else
            {
                Console.WriteLine($"[Coverage] Warning: No coverage ID for plug target {m.TargetAssembly}::{m.TargetType}::{m.TargetMethod}");
            }
        }

        return count;
    }

    /// <summary>
    /// Instruments methods in an assembly with CoverageTracker.Hit(id) probes.
    /// When <paramref name="forcedMethods"/> is null, applies normal namespace
    /// whitelist filtering. When non-null, instruments ONLY those specific methods
    /// (used for plug-targeted methods in non-Cosmos assemblies).
    /// </summary>
    private int InstrumentAssembly(string dllPath, MethodDefinition hitMethodDef,
        HashSet<(string Type, string Method)>? forcedMethods = null)
    {
        // Read the entire file into a MemoryStream first, then close the file.
        // This prevents Mono.Cecil from keeping a read lock on the file, which would
        // cause Write() to truncate the still-open file → 0-byte output.
        byte[] fileBytes = File.ReadAllBytes(dllPath);
        var memStream = new MemoryStream(fileBytes);

        AssemblyDefinition assembly;
        bool hasSymbols = false;
        try
        {
            assembly = AssemblyDefinition.ReadAssembly(memStream, new ReaderParameters
            {
                ReadSymbols = true,
                ReadingMode = ReadingMode.Immediate
            });
            hasSymbols = true;
        }
        catch
        {
            memStream = new MemoryStream(fileBytes);
            assembly = AssemblyDefinition.ReadAssembly(memStream, new ReaderParameters
            {
                ReadingMode = ReadingMode.Immediate
            });
        }

        // Import the CoverageTracker.Hit method into this assembly
        var hitMethodRef = assembly.MainModule.ImportReference(hitMethodDef);

        int methodsInstrumented = 0;
        string assemblyName = assembly.Name.Name;
        int savedNextId = _nextMethodId;
        int savedMapCount = _map.Count;
        int savedForcedCount = _forcedTargetMap.Count;

        try
        {
            foreach (var type in assembly.MainModule.GetTypes())
            {
                // For forced (plug-target) instrumentation, skip namespace whitelist;
                // for normal instrumentation, apply the usual namespace filter.
                if (forcedMethods == null && ShouldSkipType(type))
                {
                    continue;
                }

                foreach (var method in type.Methods)
                {
                    // When doing selective plug-target instrumentation, only
                    // instrument methods that appear in the forced set.
                    // Use relaxed checks (allow constructors, skip only truly
                    // uninstrumentable methods).
                    if (forcedMethods != null)
                    {
                        if (!method.HasBody || method.Body.Instructions.Count == 0)
                        {
                            continue;
                        }

                        if (method.IsAbstract || method.IsPInvokeImpl)
                        {
                            continue;
                        }

                        if (method.Body.Instructions.Count <= 1)
                        {
                            continue;
                        }

                        var sig = FormatMethodSignature(method);
                        if (!forcedMethods.Contains((type.FullName, sig)))
                        {
                            continue;
                        }
                    }
                    else
                    {
                        if (!ShouldInstrumentMethod(method))
                        {
                            continue;
                        }
                    }

                    int methodId = _nextMethodId++;

                    InstrumentMethod(method, methodId, hitMethodRef);

                    var entry = new CoverageMapEntry
                    {
                        Id = methodId,
                        Assembly = assemblyName,
                        Type = type.FullName,
                        Method = FormatMethodSignature(method),
                    };

                    // Forced-target entries go to a hidden list (used only for
                    // alias ID lookup); normal entries go to the main map.
                    if (forcedMethods != null)
                    {
                        _forcedTargetMap.Add(entry);
                    }
                    else
                    {
                        _map.Add(entry);
                    }

                    methodsInstrumented++;
                }
            }

            if (methodsInstrumented > 0)
            {
                // Write back to the original file path (safe — no open handles)
                if (hasSymbols)
                {
                    assembly.Write(dllPath, new WriterParameters { WriteSymbols = true });
                }
                else
                {
                    assembly.Write(dllPath);
                }
            }
        }
        catch
        {
            // Roll back map entries and method IDs if instrumentation failed
            _nextMethodId = savedNextId;
            if (_map.Count > savedMapCount)
            {
                _map.RemoveRange(savedMapCount, _map.Count - savedMapCount);
            }

            if (_forcedTargetMap.Count > savedForcedCount)
            {
                _forcedTargetMap.RemoveRange(savedForcedCount, _forcedTargetMap.Count - savedForcedCount);
            }

            throw;
        }
        finally
        {
            assembly.Dispose();
        }

        return methodsInstrumented;
    }

    private static void InstrumentMethod(MethodDefinition method, int methodId, MethodReference hitMethodRef)
    {
        var processor = method.Body.GetILProcessor();
        var firstInstruction = method.Body.Instructions[0];

        // Insert: ldc.i4 <methodId>; call CoverageTracker.Hit(int)
        var loadId = processor.Create(OpCodes.Ldc_I4, methodId);
        var callHit = processor.Create(OpCodes.Call, hitMethodRef);

        processor.InsertBefore(firstInstruction, loadId);
        processor.InsertBefore(firstInstruction, callHit);

        // Note: We intentionally do NOT adjust exception handler boundaries.
        // The coverage probe (ldc.i4 + call Hit) must remain OUTSIDE any try/catch/filter
        // regions. Mono.Cecil's InsertBefore automatically fixes branch targets, and
        // exception handler boundaries still correctly point at firstInstruction (now the
        // third instruction), keeping the probe outside protected regions.
    }

    private bool ShouldInstrumentAssembly(string assemblyName)
    {
        // Must match the include prefix (e.g. "Cosmos.Kernel")
        if (!assemblyName.StartsWith(_includePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Skip excluded assembly name prefixes
        foreach (var exclude in ExcludeAssemblies)
        {
            if (assemblyName.StartsWith(exclude, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whitelist check: only instrument types whose namespace starts with the
    /// include prefix. This automatically excludes ILC-injected Internal.*,
    /// System.*, and other runtime support types embedded in Cosmos assemblies.
    /// </summary>
    private bool ShouldSkipType(TypeDefinition type)
    {
        // Skip compiler-generated types
        if (type.Name.StartsWith("<") || type.Name.Contains("__"))
        {
            return true;
        }

        // Skip explicitly excluded types
        foreach (var exclude in ExcludeTypes)
        {
            if (type.FullName == exclude)
            {
                return true;
            }
        }

        // WHITELIST: only instrument types whose namespace starts with the include prefix.
        // This excludes Internal.*, System.*, and other ILC/NativeAOT runtime types
        // that get compiled into Cosmos assemblies but must not be instrumented.
        string? ns = type.Namespace;
        if (string.IsNullOrEmpty(ns))
        {
            return true; // Nested/anonymous types without namespace → skip
        }

        if (!ns.StartsWith(_includePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool ShouldInstrumentMethod(MethodDefinition method)
    {
        // Skip methods without a body
        if (!method.HasBody || method.Body.Instructions.Count == 0)
        {
            return false;
        }

        // Skip constructors (base call ordering issues)
        if (method.IsConstructor)
        {
            return false;
        }

        // Skip abstract/extern/PInvoke
        if (method.IsAbstract || method.IsPInvokeImpl)
        {
            return false;
        }

        // Skip very small methods (just ret)
        if (method.Body.Instructions.Count <= 1)
        {
            return false;
        }

        // Skip compiler-generated methods
        if (method.Name.StartsWith("<"))
        {
            return false;
        }

        return true;
    }

    private string? FindTrackerAssembly()
    {
        const string trackerDll = "Cosmos.TestRunner.Framework.dll";

        // Check assembly dir root
        var path = Path.Combine(_assemblyDir, trackerDll);
        if (File.Exists(path))
        {
            return path;
        }

        // Check ref/ subdirectory (where SetupPatcher copies ReferencePath items)
        path = Path.Combine(_assemblyDir, "ref", trackerDll);
        if (File.Exists(path))
        {
            return path;
        }

        // Check parent dir as fallback
        var parentDir = Path.GetDirectoryName(_assemblyDir);
        if (parentDir != null)
        {
            path = Path.Combine(parentDir, trackerDll);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static MethodDefinition? FindHitMethod(AssemblyDefinition trackerAssembly)
    {
        foreach (var type in trackerAssembly.MainModule.GetTypes())
        {
            if (type.FullName == "Cosmos.TestRunner.Framework.CoverageTracker")
            {
                foreach (var method in type.Methods)
                {
                    if (method.Name == "Hit" && method.Parameters.Count == 1)
                    {
                        return method;
                    }
                }
            }
        }
        return null;
    }

    private static string FormatMethodSignature(MethodDefinition method)
    {
        var parameters = string.Join(", ", method.Parameters.Select(p => p.ParameterType.Name));
        return $"{method.Name}({parameters})";
    }

    private void WriteCoverageMap()
    {
        using var writer = new StreamWriter(_outputMapPath);
        writer.WriteLine("# Coverage Map - generated by cosmos.patcher instrument-coverage");
        writer.WriteLine("# Id\tAssembly\tType\tMethod");
        foreach (var entry in _map)
        {
            writer.WriteLine($"{entry.Id}\t{entry.Assembly}\t{entry.Type}\t{entry.Method}");
        }
    }

    private struct CoverageMapEntry
    {
        public int Id;
        public string Assembly;
        public string Type;
        public string Method;
    }

    private struct PlugMapEntry
    {
        public string PlugAssembly;
        public string PlugType;
        public string PlugMethod;
        public string TargetAssembly;
        public string TargetType;
        public string TargetMethod;
    }
}
