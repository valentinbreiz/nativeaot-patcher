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
    /// Assembly name prefixes to skip entirely (test infrastructure, framework, etc.).
    /// </summary>
    private static readonly string[] ExcludeAssemblies =
    [
        "Cosmos.TestRunner",
    ];

    /// <summary>
    /// Assembly names that are entirely runtime infrastructure and must never be
    /// instrumented. Cosmos.Kernel.Core contains the memory allocator, GC, serial
    /// driver, exception handling, and NativeAOT runtime stubs — all called before
    /// CoverageTracker's static constructor can run.
    /// </summary>
    private static readonly string[] ExcludeAssembliesExact =
    [
        "Cosmos.Kernel.Core",
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

        // Process eligible assemblies in cosmos/ root and cosmos/ref/
        var dllFiles = new List<string>(Directory.GetFiles(_assemblyDir, "*.dll"));
        var refDir = Path.Combine(_assemblyDir, "ref");
        if (Directory.Exists(refDir))
            dllFiles.AddRange(Directory.GetFiles(refDir, "*.dll"));
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

        try
        {
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
                // Write back to the original file path (safe — no open handles)
                if (hasSymbols)
                    assembly.Write(dllPath, new WriterParameters { WriteSymbols = true });
                else
                    assembly.Write(dllPath);
            }
        }
        catch
        {
            // Roll back map entries and method IDs if instrumentation failed
            _nextMethodId = savedNextId;
            if (_map.Count > savedMapCount)
                _map.RemoveRange(savedMapCount, _map.Count - savedMapCount);
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
            return false;

        // Skip excluded assembly name prefixes
        foreach (var exclude in ExcludeAssemblies)
        {
            if (assemblyName.StartsWith(exclude, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // Skip exact assembly name matches (runtime-critical assemblies)
        foreach (var exclude in ExcludeAssembliesExact)
        {
            if (assemblyName.Equals(exclude, StringComparison.OrdinalIgnoreCase))
                return false;
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
            return true;

        // Skip explicitly excluded types
        foreach (var exclude in ExcludeTypes)
        {
            if (type.FullName == exclude)
                return true;
        }

        // WHITELIST: only instrument types whose namespace starts with the include prefix.
        // This excludes Internal.*, System.*, and other ILC/NativeAOT runtime types
        // that get compiled into Cosmos assemblies but must not be instrumented.
        string? ns = type.Namespace;
        if (string.IsNullOrEmpty(ns))
            return true; // Nested/anonymous types without namespace → skip

        if (!ns.StartsWith(_includePrefix, StringComparison.OrdinalIgnoreCase))
            return true;

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

        // Skip methods exported to native code via [RuntimeExport].
        // These are NativeAOT runtime entry points (RhpNewFast, memset, etc.)
        // called before static class constructors have run.
        if (HasRuntimeExportAttribute(method))
            return false;

        return true;
    }

    /// <summary>
    /// Check whether a method has a [RuntimeExport] attribute.
    /// </summary>
    private static bool HasRuntimeExportAttribute(MethodDefinition method)
    {
        if (!method.HasCustomAttributes)
            return false;

        foreach (var attr in method.CustomAttributes)
        {
            if (attr.AttributeType.Name == "RuntimeExportAttribute")
                return true;
        }

        return false;
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
