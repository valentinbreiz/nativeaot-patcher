using Cosmos.Build.API.Enum;
using Cosmos.Patcher.Debug;
using Cosmos.Patcher.Extensions;
using Cosmos.Patcher.Logging;
using Cosmos.Patcher.Resolution;
using Mono.Cecil;

namespace Cosmos.Patcher.Patching;

/// <summary>
/// Records a mapping from a plug method to its patched target method.
/// </summary>
public record PlugMapping(
    string PlugAssembly,
    string PlugType,
    string PlugMethod,
    string TargetAssembly,
    string TargetType,
    string TargetMethod);

/// <summary>
/// The PlugPatcher class is responsible for applying plugs to methods, types, and assemblies.
/// Orchestrates the patching process using specialized components.
/// </summary>
public sealed partial class PlugPatcher
{
    private readonly PlugScanner _scanner;
    private readonly IBuildLogger _log;
    private readonly MethodResolver _methodResolver;

    /// <summary>
    /// All successful plug → target method mappings recorded during patching.
    /// Only populated when <see cref="CoverageEnabled"/> is true.
    /// </summary>
    public List<PlugMapping> PlugMappings { get; } = [];

    /// <summary>
    /// When true, plug → target mappings are recorded for coverage tracking.
    /// </summary>
    public bool CoverageEnabled { get; set; }

    public PlugPatcher(PlugScanner scanner)
    {
        _log = new ConsoleBuildLogger();
        _log.Debug("Initializing PlugPatcher...");

        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));

        // Initialize components
        _methodResolver = new MethodResolver(_log);

        _log.Debug($"PlugPatcher initialized with scanner: {scanner.GetType().FullName}");
        MonoCecilExtensions.Logger = _log;
    }

    #region Public API

    /// <summary>
    /// Patches an assembly with plugs from the provided plug assemblies.
    /// </summary>
    public void PatchAssembly(AssemblyDefinition targetAssembly, params AssemblyDefinition[] plugAssemblies) =>
        PatchAssembly(targetAssembly, null, plugAssemblies);

    /// <summary>
    /// Patches an assembly with plugs from the provided plug assemblies, filtering by platform architecture.
    /// </summary>
    public void PatchAssembly(AssemblyDefinition targetAssembly, PlatformArchitecture? platformArchitecture,
        params AssemblyDefinition[] plugAssemblies)
    {
        _log.Info($"Starting patch process for assembly: {targetAssembly.FullName}");
        _log.Debug($"Plug assemblies provided: {plugAssemblies.Length}");

        try
        {
            var plugsByTarget = LoadAndGroupPlugs(plugAssemblies);

            if (plugsByTarget.Count == 0)
            {
                _log.Info("No plugs found for this assembly. Skipping patching.");
                return;
            }

            ApplyPlugsToAssembly(targetAssembly, plugsByTarget, platformArchitecture);

            _log.Debug("Updating fields, properties, and methods...");
            targetAssembly.UpdateFieldsPropertiesAndMethods(true);
            _log.Info($"Assembly {targetAssembly.Name} updated successfully");
        }
        catch (Exception ex)
        {
            _log.Error($"CRITICAL ERROR patching assembly: {ex}");
            throw;
        }

        _log.Info($"Completed patching assembly: {targetAssembly.FullName}");
    }

    /// <summary>
    /// Patches a specific type with plugs from the provided plug assemblies.
    /// </summary>
    public void PatchType(TypeDefinition targetType, params AssemblyDefinition[] plugAssemblies)
    {
        _log.Info($"Starting patch process for type: {targetType.FullName}");

        if (plugAssemblies.Length == 0)
        {
            _log.Error("ERROR: No plug assemblies provided");
            return;
        }

        _log.Debug("Loading plugs from provided assemblies...");
        List<TypeDefinition> plugs = _scanner.LoadPlugs(targetType, plugAssemblies);
        _log.Info($"Found {plugs.Count} plug types to process");

        if (plugs.Count == 0)
        {
            return;
        }

        foreach (TypeDefinition plugType in plugs)
        {
            _log.Info($"Processing members for plug type: {plugType.FullName}");
            ProcessPlugMembers(targetType, plugType);
        }

        _log.Info($"Completed processing type: {targetType.FullName}");
    }


    #endregion

    #region Private Methods

    /// <summary>
    /// Loads plugs from assemblies and groups them by target type name.
    /// </summary>
    private Dictionary<string, List<TypeDefinition>> LoadAndGroupPlugs(AssemblyDefinition[] plugAssemblies)
    {
        List<TypeDefinition> allPlugs = _scanner.LoadPlugs(plugAssemblies);
        Dictionary<string, List<TypeDefinition>> plugsByTarget = [];

        foreach (TypeDefinition plug in allPlugs)
        {
            CustomAttribute? plugAttr = plug.GetCustomAttribute(PlugScanner.PlugAttributeFullName);
            if (plugAttr == null)
            {
                continue;
            }

            string? targetName = plugAttr.GetArgument<string>(named: "TargetName") ??
                                 plugAttr.GetArgument<string>(named: "Target");

            if (string.IsNullOrWhiteSpace(targetName))
            {
                _log.Warn($"PlugAttribute on {plug.FullName} missing TargetName/Target argument. Skipping.");
                continue;
            }

            if (!plugsByTarget.TryGetValue(targetName, out List<TypeDefinition>? list))
            {
                list = [];
                plugsByTarget[targetName] = list;
            }

            list!.Add(plug);
        }

        return plugsByTarget;
    }

    /// <summary>
    /// Applies grouped plugs to the target assembly.
    /// </summary>
    private void ApplyPlugsToAssembly(AssemblyDefinition targetAssembly,
        Dictionary<string, List<TypeDefinition>> plugsByTarget,
        PlatformArchitecture? platformArchitecture)
    {
        foreach ((string targetName, List<TypeDefinition> plugTypes) in plugsByTarget)
        {
            TypeDefinition? targetType = targetAssembly.MainModule.GetType(targetName)
                                         ?? targetAssembly.MainModule.Types.FirstOrDefault(t => t.FullName == targetName);

            if (targetType == null)
            {
                _log.Warn($"Target type not found: {targetName}");
                continue;
            }

            if (targetType.HasCustomAttribute(PlugScanner.PlugAttributeFullName))
            {
                _log.Info($"Skipping type marked with PlugAttribute: {targetType.FullName}");
                continue;
            }

            _log.Info($"Processing type: {targetType.FullName}");

            foreach (TypeDefinition plugType in plugTypes)
            {
                try
                {
                    ProcessPlugMembers(targetType, plugType, platformArchitecture);
                    _log.Debug($"Successfully processed plug {plugType.FullName} for type {targetType.FullName}");
                }
                catch (Exception ex)
                {
                    _log.Error($"ERROR processing type {targetType.FullName} with plug {plugType.FullName}: {ex}");
                }
            }
        }
    }

    /// <summary>
    /// Processes all members of a plug type and applies them to the target type.
    /// </summary>
    private void ProcessPlugMembers(TypeDefinition targetType, TypeDefinition plugType,
        PlatformArchitecture? platformArchitecture = null)
    {
        _log.Debug($"Processing members for {plugType.FullName}");

        foreach (IMemberDefinition member in plugType.GetMembers())
        {
            _log.Debug($"Plug member: {member.Name} ({member.GetType().Name})");

            // Check platform-specific filtering
            if (!ShouldProcessMember(member, plugType, platformArchitecture))
            {
                continue;
            }

            CustomAttribute? plugMemberAttr = member.GetCustomAttribute(PlugScanner.PlugMemberAttributeFullName);
            if (plugMemberAttr == null)
            {
                _log.Debug($"Skipping member without PlugMemberAttribute: {member.Name}");
                continue;
            }

            string? targetMemberName = plugMemberAttr.GetArgument(named: "TargetName", defaultValue: member.Name) ??
                                       plugMemberAttr.GetArgument(named: "Target", defaultValue: member.Name);

            _log.Debug($"Looking for target member: {targetMemberName}");

            try
            {
                plugMemberAttr.ImportReferences(targetType.Module);
                ProcessMember(targetType, member, targetMemberName);
            }
            catch (Exception ex)
            {
                _log.Error($"ERROR processing member {member.FullName}: {ex}");
                DumpDebugInfo(targetType, targetMemberName, member);
            }
        }

        // Dump plug type after processing
        DebugHelpers.DumpTypeMembers(_log, plugType);
    }

    /// <summary>
    /// Checks if a member should be processed based on platform architecture.
    /// </summary>
    private bool ShouldProcessMember(IMemberDefinition member, TypeDefinition plugType,
        PlatformArchitecture? platformArchitecture)
    {
        CustomAttribute? plugMemberAttr = member.GetCustomAttribute(PlugScanner.PlugMemberAttributeFullName);
        CustomAttribute? platformSpecAttr = member.GetCustomAttribute(PlugScanner.PlatformSpecificAttributeFullName);
        PlatformArchitecture? memberArchitecture = platformSpecAttr?.GetArgument<PlatformArchitecture>(named: "Architecture");



        if (platformSpecAttr != null && platformArchitecture != null && memberArchitecture != null &&
            !memberArchitecture.Value.HasFlag(platformArchitecture.Value))
        {
            if (plugMemberAttr != null)
            {
                _log.Debug($"Skipping plugging member due to platform mismatch: {member.Name}");
            }
            else
            {
                _log.Info($"Removing member due to platform mismatch: {member.Name}");
                RemoveMember(plugType, member);
            }
            return false;
        }

        return true;
    }

    /// <summary>
    /// Processes a single member (method, property, or field).
    /// </summary>
    private void ProcessMember(TypeDefinition targetType, IMemberDefinition member, string? targetMemberName)
    {
        switch (member)
        {
            case MethodDefinition plugMethod:
                ResolveAndPatchMethod(targetType, plugMethod, targetMemberName);
                break;
            case PropertyDefinition plugProperty:
                PatchProperty(targetType, plugProperty, targetMemberName);
                break;
            case FieldDefinition plugField:
                PatchField(targetType, plugField, targetMemberName);
                break;
            default:
                _log.Warn($"Unsupported member type: {member.Name}");
                break;
        }
    }

    /// <summary>
    /// Resolves and patches a method or constructor.
    /// </summary>
    private void ResolveAndPatchMethod(TypeDefinition targetType, MethodDefinition plugMethod, string? targetMethodName)
    {
        _log.Debug($"Starting method resolution for {plugMethod.FullName}");

        if (plugMethod.Name is "Ctor" or "CCtor")
        {
            MethodDefinition? ctor = _methodResolver.ResolveConstructor(targetType, plugMethod);
            if (ctor != null)
            {
                _log.Debug($"Target prototype: {DebugHelpers.FormatMethodSignature(ctor)}");
                _log.Debug($"Plug prototype: {DebugHelpers.FormatMethodSignature(plugMethod)}");
                PatchMethod(ctor, plugMethod);
                RecordPlugMapping(plugMethod, ctor);
            }
            return;
        }

        MethodDefinition? targetMethod = _methodResolver.ResolveMethod(targetType, plugMethod, targetMethodName ?? plugMethod.Name);
        if (targetMethod != null)
        {
            _log.Debug($"Target prototype: {DebugHelpers.FormatMethodSignature(targetMethod)}");
            PatchMethod(targetMethod, plugMethod);
            RecordPlugMapping(plugMethod, targetMethod);
        }
        else
        {
            _methodResolver.DumpOverloads(targetType, targetMethodName ?? plugMethod.Name, plugMethod);
        }
    }

    /// <summary>
    /// Records a plug → target mapping for coverage tracking.
    /// </summary>
    private void RecordPlugMapping(MethodDefinition plugMethod, MethodDefinition targetMethod)
    {
        if (!CoverageEnabled)
        {
            return;
        }

        PlugMappings.Add(new PlugMapping(
            PlugAssembly: plugMethod.DeclaringType.Module.Assembly.Name.Name,
            PlugType: plugMethod.DeclaringType.FullName,
            PlugMethod: DebugHelpers.FormatMethodSignature(plugMethod),
            TargetAssembly: targetMethod.DeclaringType.Module.Assembly.Name.Name,
            TargetType: targetMethod.DeclaringType.FullName,
            TargetMethod: DebugHelpers.FormatMethodSignature(targetMethod)
        ));
    }

    /// <summary>
    /// Removes a member from a type.
    /// </summary>
    private void RemoveMember(TypeDefinition targetType, IMemberDefinition member)
    {
        try
        {
            switch (member)
            {
                case MethodDefinition method:
                    RemoveMethod(targetType, method);
                    break;
                case PropertyDefinition property:
                    RemoveProperty(targetType, property);
                    break;
                case FieldDefinition field:
                    targetType.Fields.Remove(field);
                    break;
                default:
                    throw new InvalidOperationException("Unsupported member type");
            }

            _log.Debug($"Removed member: {member.Name}");
        }
        catch (Exception ex)
        {
            _log.Error($"ERROR removing member {member.Name}: {ex}");
            throw;
        }
    }


    /// <summary>
    /// Dumps debug information when an error occurs.
    /// </summary>
    private void DumpDebugInfo(TypeDefinition targetType, string? targetName, IMemberDefinition member)
    {
        _log.Debug("--- DEBUG DUMP BEGIN ---");
        try
        {
            _log.Debug($"PlugMember.TargetName = {targetName}");
            if (member is MethodDefinition plugMethod)
            {

                _methodResolver.DumpOverloads(targetType, targetName ?? member.Name, plugMethod);
            }
        }
        catch (Exception e2)
        {
            _log.Warn($"WARN while dumping debug info: {e2}");
        }
        _log.Debug("--- DEBUG DUMP END ---");
    }

    #endregion
}
