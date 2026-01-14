using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cosmos.Patcher.Logging;
using Mono.Cecil;

namespace Cosmos.Patcher;

public sealed class PlugScanner(IBuildLogger? logger = null)
{
    public const string PlugAttributeFullName = "Cosmos.Build.API.Attributes.PlugAttribute";

    public const string PlugMemberAttributeFullName = "Cosmos.Build.API.Attributes.PlugMemberAttribute";

    public const string PlatformSpecificAttributeFullName = "Cosmos.Build.API.Attributes.PlatformSpecificAttribute";

    private readonly IBuildLogger _log = logger ?? new ConsoleBuildLogger();

    public List<TypeDefinition> LoadPlugs(params AssemblyDefinition[] assemblies) => LoadPlugs(null, assemblies);

    public List<TypeDefinition> LoadPlugs(TypeDefinition? targetType = null, params AssemblyDefinition[] assemblies)
    {
        List<TypeDefinition> output =
        [
            ..assemblies
                .SelectMany(assembly => assembly.Modules)
                .SelectMany(module => module.Types)
                .Where(type =>
                {
                    CustomAttribute? plugAttr = type.CustomAttributes
                        .FirstOrDefault(a => a.AttributeType.FullName == PlugAttributeFullName);
                    if (plugAttr == null)
                        return false;

                    if (targetType == null)
                        return true;

                    string? targetTypeName = plugAttr.GetArgument<string>(named: "TargetName");
                    return targetType.FullName == targetTypeName;
                })
        ];

        foreach (TypeDefinition type in output)
            _log.Debug($"Plug found: {type.Name}");

        return output;
    }

    public List<MethodDefinition> LoadPlugMethods(TypeDefinition plugType) =>
        [.. plugType.Methods.Where(m => m.IsPublic && m.IsStatic)];

    public IEnumerable<string> FindPluggedAssemblies(IEnumerable<string> plugAssemblyPaths,
                                                      IEnumerable<string> candidateAssemblyPaths)
    {
        HashSet<string> targetTypes = [];
        foreach (string plugPath in plugAssemblyPaths)
        {
            if (!File.Exists(plugPath))
                continue;

            try
            {
                using AssemblyDefinition plugAsm = AssemblyDefinition.ReadAssembly(plugPath);
                foreach (TypeDefinition type in plugAsm.MainModule.Types)
                {
                    CustomAttribute? attr = type.CustomAttributes
                        .FirstOrDefault(a => a.AttributeType.FullName == PlugAttributeFullName);
                    if (attr == null)
                        continue;

                    string? target = attr.GetArgument<string>(named: "TargetName");
                    if (!string.IsNullOrEmpty(target))
                        targetTypes.Add(target);
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"[Scanner] Skipping non-.NET or invalid assembly '{plugPath}': {ex.Message}");
                continue;
            }
        }

        HashSet<string> added = new(StringComparer.OrdinalIgnoreCase);
        foreach (string candidatePath in candidateAssemblyPaths)
        {
            if (!File.Exists(candidatePath))
                continue;

            using AssemblyDefinition? asm = TryReadAssembly(candidatePath);
            if (asm == null)
                continue; // skip invalid/native binaries

            foreach (string t in targetTypes)
            {
                TypeDefinition? type = asm.MainModule.GetType(t) ??
                                        asm.MainModule.Types.FirstOrDefault(x => x.FullName == t);
                if (type != null)
                {
                    if (added.Add(candidatePath))
                        yield return candidatePath;
                    break;
                }
            }
        }

        static AssemblyDefinition? TryReadAssembly(string path)
        {
            try
            {
                return AssemblyDefinition.ReadAssembly(path);
            }
            catch
            {
                return null;
            }
        }
    }
}
