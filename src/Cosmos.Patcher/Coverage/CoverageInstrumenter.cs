using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Cosmos.Patcher.Coverage;

/// <summary>
/// Instruments managed assemblies with method-level coverage tracking.
/// For each eligible method, inserts a CoverageTracker.Hit(id) call at method entry.
/// </summary>
public class CoverageInstrumenter
{
    private readonly string _assemblyDir;
    private readonly string _outputMapPath;
    private readonly string _includePrefix;

    private int _nextMethodId;
    private readonly List<CoverageMapEntry> _map = [];

    /// <summary>
    /// Assembly name prefixes to skip instrumentation (test code, the tracker itself).
    /// </summary>
    private static readonly string[] ExcludeAssemblies =
    [
        "Cosmos.TestRunner",
        "System.",
        "Internal.",
        "Microsoft.",
    ];

    /// <summary>
    /// Types to skip instrumentation to avoid recursion or early-boot issues.
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

        // Process each eligible assembly
        var dllFiles = Directory.GetFiles(_assemblyDir, "*.dll");
        int instrumentedAssemblies = 0;

        foreach (var dllPath in dllFiles)
        {
            var assemblyName = Path.GetFileNameWithoutExtension(dllPath);

            if (!ShouldInstrumentAssembly(assemblyName))
                continue;

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

        // Write coverage map
        WriteCoverageMap();

        Console.WriteLine($"[Coverage] Total: {_nextMethodId} methods instrumented across {instrumentedAssemblies} assemblies");
        return _nextMethodId;
    }

    private int InstrumentAssembly(string dllPath, MethodDefinition hitMethodDef)
    {
        // Try to load with symbols
        AssemblyDefinition assembly;
        bool hasSymbols = false;
        try
        {
            assembly = AssemblyDefinition.ReadAssembly(dllPath, new ReaderParameters { ReadSymbols = true });
            hasSymbols = true;
        }
        catch
        {
            assembly = AssemblyDefinition.ReadAssembly(dllPath);
        }

        // Import the CoverageTracker.Hit method into this assembly
        var hitMethodRef = assembly.MainModule.ImportReference(hitMethodDef);

        int methodsInstrumented = 0;
        string assemblyName = assembly.Name.Name;

        foreach (var type in assembly.MainModule.GetTypes())
        {
            if (ShouldSkipType(type))
                continue;

            foreach (var method in type.Methods)
            {
                if (!ShouldInstrumentMethod(method))
                    continue;

                int methodId = _nextMethodId++;

                InstrumentMethod(method, methodId, hitMethodRef);

                _map.Add(new CoverageMapEntry
                {
                    Id = methodId,
                    Assembly = assemblyName,
                    Type = type.FullName,
                    Method = FormatMethodSignature(method),
                });

                methodsInstrumented++;
            }
        }

        if (methodsInstrumented > 0)
        {
            // Write back
            if (hasSymbols)
                assembly.Write(dllPath, new WriterParameters { WriteSymbols = true });
            else
                assembly.Write(dllPath);
        }

        assembly.Dispose();
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

        // Update exception handler boundaries that pointed to the original first instruction
        foreach (var handler in method.Body.ExceptionHandlers)
        {
            if (handler.TryStart == firstInstruction)
                handler.TryStart = loadId;
            if (handler.HandlerStart == firstInstruction)
                handler.HandlerStart = loadId;
            if (handler.FilterStart == firstInstruction)
                handler.FilterStart = loadId;
        }
    }

    private bool ShouldInstrumentAssembly(string assemblyName)
    {
        if (!assemblyName.StartsWith(_includePrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        foreach (var exclude in ExcludeAssemblies)
        {
            if (assemblyName.StartsWith(exclude, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static bool ShouldSkipType(TypeDefinition type)
    {
        // Skip compiler-generated types
        if (type.Name.StartsWith("<") || type.Name.Contains("__"))
            return true;

        foreach (var exclude in ExcludeTypes)
        {
            if (type.FullName == exclude)
                return true;
        }

        return false;
    }

    private static bool ShouldInstrumentMethod(MethodDefinition method)
    {
        // Skip methods without a body
        if (!method.HasBody || method.Body.Instructions.Count == 0)
            return false;

        // Skip constructors (base call ordering issues)
        if (method.IsConstructor)
            return false;

        // Skip abstract/extern/PInvoke
        if (method.IsAbstract || method.IsPInvokeImpl)
            return false;

        // Skip very small methods (just ret)
        if (method.Body.Instructions.Count <= 1)
            return false;

        // Skip compiler-generated methods
        if (method.Name.StartsWith("<"))
            return false;

        return true;
    }

    private string? FindTrackerAssembly()
    {
        const string trackerDll = "Cosmos.TestRunner.Framework.dll";

        // Check assembly dir root
        var path = Path.Combine(_assemblyDir, trackerDll);
        if (File.Exists(path))
            return path;

        // Check ref/ subdirectory (where SetupPatcher copies ReferencePath items)
        path = Path.Combine(_assemblyDir, "ref", trackerDll);
        if (File.Exists(path))
            return path;

        // Check parent dir as fallback
        var parentDir = Path.GetDirectoryName(_assemblyDir);
        if (parentDir != null)
        {
            path = Path.Combine(parentDir, trackerDll);
            if (File.Exists(path))
                return path;
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
                        return method;
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
}
