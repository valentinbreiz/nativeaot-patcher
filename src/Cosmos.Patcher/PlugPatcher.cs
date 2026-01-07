using Cosmos.Build.API.Attributes;
using Cosmos.Build.API.Enum;
using Cosmos.Patcher.Extensions;
using Cosmos.Patcher.Logging;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Mono.Collections.Generic;
using MonoMod.Utils;

namespace Cosmos.Patcher;

/// <summary>
/// The PlugPatcher class is responsible for applying plugs to methods, types, and assemblies.
/// </summary>
public sealed class PlugPatcher
{
    private readonly PlugScanner _scanner;
    private readonly IBuildLogger _log;

    public PlugPatcher(PlugScanner scanner)
    {
        _log = new ConsoleBuildLogger();
        _log.Debug("Initializing PlugPatcher...");
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _log.Debug($"PlugPatcher initialized with scanner: {scanner.GetType().FullName}");
        MonoCecilExtensions.Logger = _log;
    }

    public void PatchAssembly(AssemblyDefinition targetAssembly, params AssemblyDefinition[] plugAssemblies) =>
        PatchAssembly(targetAssembly, null, plugAssemblies);

    public void PatchAssembly(AssemblyDefinition targetAssembly, PlatformArchitecture? platformArchitecture,
        params AssemblyDefinition[] plugAssemblies)
    {
        _log.Info($"Starting patch process for assembly: {targetAssembly.FullName}");
        _log.Debug($"Plug assemblies provided: {plugAssemblies.Length}");

        try
        {
            List<TypeDefinition> allPlugs = _scanner.LoadPlugs(plugAssemblies);
            Dictionary<string, List<TypeDefinition>> plugsByTarget = [];

            foreach (TypeDefinition plug in allPlugs)
            {
                CustomAttribute? plugAttr = plug.GetCustomAttribute(PlugScanner.PlugAttributeFullName);
                if (plugAttr == null)
                    continue;

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

            if (plugsByTarget.Count == 0)
            {
                _log.Info("No plugs found for this assembly. Skipping patching.");
                return;
            }

            foreach ((string targetName, List<TypeDefinition> plugTypes) in plugsByTarget)
            {
                TypeDefinition? targetType = targetAssembly.MainModule.GetType(targetName)
                                             ?? targetAssembly.MainModule.Types.FirstOrDefault(t =>
                                                 t.FullName == targetName);

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

            _log.Debug("Updating fields, properties, and methods...");
            targetAssembly.UpdateFieldsPropertiesAndMethods(true);
            _log.Info($" Assembly {targetAssembly.Name} updated successfully");
        }
        catch (Exception ex)
        {
            _log.Error($"CRITICAL ERROR patching assembly: {ex}");
            throw;
        }

        _log.Info($"Completed patching assembly: {targetAssembly.FullName}");
    }

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
            return;

        foreach (TypeDefinition plugType in plugs)
        {
            _log.Info($"Processing members for plug type: {plugType.FullName}");
            ProcessPlugMembers(targetType, plugType);
        }

        _log.Info($"Completed processing type: {targetType.FullName}");
    }

    private void ProcessPlugMembers(TypeDefinition targetType, TypeDefinition plugType,
        PlatformArchitecture? platformArchitecture = null)
    {
        _log.Debug($"Processing methods for {plugType.FullName}");
        foreach (IMemberDefinition member in plugType.GetMembers())
        {
            _log.Debug($"Plug member: {member.Name} ({member.GetType().Name})");

            _log.Debug($"Processing method: {member.FullName}");
            CustomAttribute? plugMemberAttr = member.GetCustomAttribute(PlugScanner.PlugMemberAttributeFullName);
            CustomAttribute? platformSpecAttr =
                member.GetCustomAttribute(PlugScanner.PlatformSpecificAttributeFullName);
            PlatformArchitecture? memberArchitecture =
                platformSpecAttr?.GetArgument<PlatformArchitecture>(named: "Architecture");

            if (platformSpecAttr != null && platformArchitecture != null && memberArchitecture != null &&
                !memberArchitecture.Value.HasFlag(platformArchitecture.Value))
            {
                if (plugMemberAttr != null)
                    _log.Debug(
                        $"Skipping plugging member due to platform mismatch: {member.Name} (Member Target Architecture: {memberArchitecture}, Target Architecture: {platformArchitecture})");
                else
                {
                    _log.Info(
                        $"Removing member due to platform mismatch: {member.Name} (Member Target Architecture: {memberArchitecture}, Target Architecture: {platformArchitecture})");
                    RemoveMember(plugType, member);
                }

                continue;
            }

            if (plugMemberAttr == null)
            {
                _log.Debug($"Skipping method without PlugMemberAttribute: {member.Name}");
                continue;
            }

            string? targetMemberName = plugMemberAttr.GetArgument(named: "TargetName", defaultValue: member.Name) ??
                                       plugMemberAttr.GetArgument(named: "Target", defaultValue: member.Name);
            _log.Debug($"Looking for target member: {targetMemberName}");
            try
            {
                plugMemberAttr.ImportReferences(targetType.Module);
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
                        _log.Warn($"Unsupported member type for member {member.Name} and/or target member not found");
                        break;
                }
            }
            catch (Exception ex)
            {
                _log.Error($"ERROR processing method {member.FullName}: {ex}");
                // --- DEBUG DUMP ---
                _log.Debug("--- DEBUG DUMP BEGIN ---");
                // _log.Debug(Fmt(member));
                try
                {
                    string? targetName = plugMemberAttr.GetArgument(named: "TargetName", defaultValue: member.Name) ??
                                         plugMemberAttr.GetArgument(named: "Target", defaultValue: member.Name);
                    _log.Debug($"PlugMember.TargetName = {targetName}");
                    DumpOverloads(targetType, targetName, member);
                }
                catch (Exception e2)
                {
                    _log.Warn($"WARN while dumping overloads: {e2}");
                }

                _log.Debug("--- DEBUG DUMP END ---");
            }
        }

        // Dump plugType after plugging
        _log.Debug($"--- Dumping plugType after plugging: {plugType.FullName} ---");
        foreach (IMemberDefinition member in plugType.GetMembers())
        {
            _log.Debug($"PlugType Member: {member.GetType().Name} {member.FullName}");
            if (member is MethodDefinition { HasBody: true } method)
            {
                DumpIL(method);
            }
        }

        _log.Debug($"--- End dump for plugType: {plugType.FullName} ---");
    }

    private void ResolveAndPatchMethod(TypeDefinition targetType, MethodDefinition plugMethod,
        string? targetMethodName = "")
    {
        _log.Debug($"Starting method resolution for {plugMethod.FullName}");
        bool isInstance = plugMethod.Parameters.Any(p => p.Name == "aThis");

        if (plugMethod.Name is "Ctor" or "CCtor")
        {
            _log.Debug($"Handling constructor plug (Instance: {isInstance})");

            // Find constructor with matching parameter count AND types
            MethodDefinition? ctor = null;
            int plugStartIndex = isInstance ? 1 : 0;

            foreach (var m in targetType.Methods.Where(m => m.IsConstructor))
            {
                if (plugMethod.Name == "CCtor" && !m.IsStatic)
                    continue;

                int expectedParamCount = isInstance ? plugMethod.Parameters.Count - 1 : plugMethod.Parameters.Count;
                if (m.Parameters.Count != expectedParamCount)
                    continue;

                bool typesMatch = true;
                for (int i = 0; i < m.Parameters.Count; i++)
                {
                    if (m.Parameters[i].ParameterType.FullName != plugMethod.Parameters[i + plugStartIndex].ParameterType.FullName)
                    {
                        typesMatch = false;
                        break;
                    }
                }

                if (typesMatch)
                {
                    ctor = m;
                    break;
                }
            }

            if (ctor != null)
            {
                _log.Debug($"Found target constructor: {ctor.FullName}");
                _log.Debug($"Target prototype: {FmtMethod(ctor)}");
                _log.Debug($"Plug   prototype: {FmtMethod(plugMethod)}");
                PatchMethod(ctor, plugMethod);
            }
            else
            {
                _log.Warn($"No matching constructor found for {plugMethod.FullName}");
                _log.Debug(
                    $"Plug parameters: {string.Join(", ", plugMethod.Parameters.Select(p => p.ParameterType + " " + p.Name))}");
            }

            return;
        }

        _log.Debug($"Resolving method: {targetMethodName} (Instance: {isInstance})");
        _log.Debug($"Plug prototype: {FmtMethod(plugMethod)}");

        // Find method with matching name, parameter count, AND parameter types
        MethodDefinition? targetMethod = null;

        foreach (MethodDefinition? m in targetType.Methods.Where(m => m.Name == targetMethodName))
        {
            // Check parameter count
            int expectedParamCount = isInstance ? plugMethod.Parameters.Count - 1 : plugMethod.Parameters.Count;
            if (m.Parameters.Count != expectedParamCount)
                continue;

            // Check parameter types match
            bool typesMatch = true;
            int plugStartIndex = isInstance ? 1 : 0; // Skip 'aThis' parameter if instance plug

            for (int i = 0; i < m.Parameters.Count; i++)
            {
                ParameterDefinition targetParam = m.Parameters[i];
                ParameterDefinition plugParam = plugMethod.Parameters[i + plugStartIndex];

                // Compare type names (you might need more sophisticated type comparison)
                if (targetParam.ParameterType.FullName != plugParam.ParameterType.FullName)
                {
                    typesMatch = false;
                    break;
                }
            }

            if (typesMatch)
            {
                targetMethod = m;
                break;
            }
        }

        if (targetMethod != null)
        {
            _log.Debug($"Found target method: {targetMethod.FullName}");
            _log.Debug($"Parameters: {targetMethod.Parameters.Count}");
            _log.Debug($"Target prototype: {FmtMethod(targetMethod)}");
            PatchMethod(targetMethod, plugMethod);
        }
        else
        {
            _log.Warn($"Target method not found: {targetMethodName}");
            _log.Debug($"Expected parameters: {plugMethod.Parameters.Count - (isInstance ? 1 : 0)}");
            _log.Debug(
                $"Expected parameter types: {string.Join(", ", plugMethod.Parameters.Skip(isInstance ? 1 : 0).Select(p => p.ParameterType.FullName))}");
            DumpOverloads(targetType, targetMethodName, plugMethod);
        }
    }

    public void PatchMethod(MethodDefinition targetMethod, MethodDefinition plugMethod, bool instance = false)
    {
        _log.Info($"Starting method patch: {targetMethod.FullName} <- {plugMethod.FullName}");
        _log.Debug(
            $"Target method attributes: Static={targetMethod.IsStatic}, Constructor={targetMethod.IsConstructor}");
        _log.Debug($"Plug method parameters: {plugMethod.Parameters.Count}");

        targetMethod.Body ??= new MethodBody(targetMethod);
        ILProcessor processor = targetMethod.Body.GetILProcessor();
        _log.Debug($"Original instruction count: {targetMethod.Body.Instructions.Count}");

        if (targetMethod.IsConstructor)
        {
            Instruction? call = targetMethod.Body.Instructions.FirstOrDefault(i => i.OpCode == OpCodes.Call
                && i.Operand is MethodReference { Name: ".ctor" } method
                && (method.DeclaringType == targetMethod.DeclaringType
                    || method.DeclaringType == targetMethod.DeclaringType.BaseType));

            if (call is not null)
            {
                int index = targetMethod.Body.Instructions.IndexOf(call);
                int instructionsToKeep = index + 1;
                _log.Debug(
                    $"Base Constructor Call found at index {index}, preserving {instructionsToKeep} instructions");
                while (targetMethod.Body.Instructions.Count > instructionsToKeep)
                {
                    processor.RemoveAt(instructionsToKeep);
                }
            }
            else
            {
                _log.Debug("No Base Constructor Call found in constructor, clearing all instructions");
                targetMethod.Body.Instructions.Clear();
            }
        }
        else
        {
            _log.Debug("Clearing non-constructor method body");
            targetMethod.Body.Instructions.Clear();
        }

        bool isInstance = plugMethod.Parameters.Any(p => p.Name == "aThis") || instance;
        _log.Debug($"Instance method: {isInstance}, Target static: {targetMethod.IsStatic}");

        if ((isInstance && !targetMethod.IsStatic) || targetMethod.IsConstructor)
        {
            // Clear existing variables and exception handlers before copying plug's
            targetMethod.Body.Variables.Clear();
            targetMethod.Body.ExceptionHandlers.Clear();

            // Copy variables with imported types (using SafeImportType to fix self-references)
            foreach (VariableDefinition? variable in plugMethod.Body.Variables)
            {
                targetMethod.Body.Variables.Add(
                    new VariableDefinition(SafeImportType(targetMethod.Module, variable.VariableType))
                );
            }

            _log.Debug($"Cloning {plugMethod.Body.Instructions.Count} instructions");

            // Build instruction mapping for branch target remapping
            var instructionMap = new Dictionary<Instruction, Instruction>();

            foreach (Instruction instruction in plugMethod.Body.Instructions)
            {
                try
                {
                    Instruction clone = instruction.Clone();
                    clone.Operand = instruction.Operand switch
                    {
                        MethodReference m => SafeImportMethod(targetMethod.Module, m),
                        FieldReference f => SafeImportField(targetMethod.Module, f),
                        TypeReference t => SafeImportType(targetMethod.Module, t),
                        MemberReference mr => targetMethod.Module.ImportReference(mr),
                        ParameterDefinition p => RemapParameter(p, plugMethod, targetMethod, isInstance),
                        _ => instruction.Operand
                    };
                    processor.Append(clone);
                    instructionMap[instruction] = clone;
                    _log.Debug($"Cloned instruction {instruction} -> {clone}");
                }
                catch (Exception ex)
                {
                    _log.Error($"ERROR cloning instruction {instruction}: {ex}");
                    throw;
                }
            }

            // Fix branch targets to point to cloned instructions
            FixBranchTargets(instructionMap);

            // Copy method body properties
            targetMethod.Body.InitLocals = plugMethod.Body.InitLocals;
            targetMethod.Body.MaxStackSize = plugMethod.Body.MaxStackSize;
        }
        else
        {
            _log.Debug("Performing full method swap with proper imports");
            try
            {
                // Clear existing body completely
                targetMethod.Body.Clear();

                // Copy variables with imported types (using SafeImportType to fix self-references)
                foreach (VariableDefinition? variable in plugMethod.Body.Variables)
                {
                    targetMethod.Body.Variables.Add(
                        new VariableDefinition(SafeImportType(targetMethod.Module, variable.VariableType))
                    );
                }

                // Clone and import all instructions
                var instructionMap = new Dictionary<Instruction, Instruction>();

                foreach (Instruction instruction in plugMethod.Body.Instructions)
                {
                    Instruction clone = instruction.Clone();

                    // Import all member references using safe import to fix self-references
                    clone.Operand = instruction.Operand switch
                    {
                        MethodReference m => SafeImportMethod(targetMethod.Module, m),
                        FieldReference f => SafeImportField(targetMethod.Module, f),
                        TypeReference t => SafeImportType(targetMethod.Module, t),
                        MemberReference mr => targetMethod.Module.ImportReference(mr),
                        ParameterDefinition p => RemapParameter(p, plugMethod, targetMethod, isInstance),
                        _ => instruction.Operand
                    };

                    processor.Append(clone);
                    instructionMap[instruction] = clone;
                    _log.Debug($"Cloned instruction {instruction} -> {clone}");
                }

                // Fix branch targets to point to cloned instructions
                FixBranchTargets(instructionMap);

                // Copy exception handlers if any
                if (plugMethod.Body.HasExceptionHandlers)
                {
                    foreach (ExceptionHandler? handler in plugMethod.Body.ExceptionHandlers)
                    {
                        var newHandler = new ExceptionHandler(handler.HandlerType);

                        // Find corresponding instructions in the new body
                        int tryStartIndex = plugMethod.Body.Instructions.IndexOf(handler.TryStart);
                        int tryEndIndex = plugMethod.Body.Instructions.IndexOf(handler.TryEnd);
                        int handlerStartIndex = plugMethod.Body.Instructions.IndexOf(handler.HandlerStart);

                        newHandler.TryStart = targetMethod.Body.Instructions[tryStartIndex];
                        newHandler.TryEnd = targetMethod.Body.Instructions[tryEndIndex];
                        newHandler.HandlerStart = targetMethod.Body.Instructions[handlerStartIndex];

                        if (handler.HandlerEnd != null)
                        {
                            int handlerEndIndex = plugMethod.Body.Instructions.IndexOf(handler.HandlerEnd);
                            newHandler.HandlerEnd = targetMethod.Body.Instructions[handlerEndIndex];
                        }

                        if (handler.CatchType != null)
                        {
                            newHandler.CatchType = SafeImportType(targetMethod.Module, handler.CatchType);
                        }

                        if (handler.FilterStart != null)
                        {
                            int filterStartIndex = plugMethod.Body.Instructions.IndexOf(handler.FilterStart);
                            newHandler.FilterStart = targetMethod.Body.Instructions[filterStartIndex];
                        }

                        targetMethod.Body.ExceptionHandlers.Add(newHandler);
                    }
                }

                // Copy other method body properties
                targetMethod.Body.InitLocals = plugMethod.Body.InitLocals;
                targetMethod.Body.MaxStackSize = plugMethod.Body.MaxStackSize;
                DumpIL(targetMethod);
                _log.Debug(
                    $"Cloned {targetMethod.Body.Instructions.Count} instructions with {targetMethod.Body.Variables.Count} variables and {targetMethod.Body.ExceptionHandlers.Count} exception handlers");
            }
            catch (Exception ex)
            {
                _log.Error($"ERROR during method swap: {ex}");
                throw;
            }
        }

        // Remove P/Invoke metadata so the runtime treats the method as managed
        if (targetMethod.IsPInvokeImpl || targetMethod.PInvokeInfo is not null)
        {
            _log.Debug("Removing P/Invoke metadata");
            targetMethod.PInvokeInfo = null;
            targetMethod.Attributes &= ~MethodAttributes.PInvokeImpl;
            targetMethod.ImplAttributes &= ~MethodImplAttributes.PreserveSig;
            targetMethod.ImplAttributes &= ~MethodImplAttributes.InternalCall;
            targetMethod.ImplAttributes &= ~MethodImplAttributes.Native;
            targetMethod.ImplAttributes &= ~MethodImplAttributes.Unmanaged;
            targetMethod.ImplAttributes &= ~MethodImplAttributes.Runtime;
        }

        if (targetMethod.Body.Instructions.Count == 0 || targetMethod.Body.Instructions[^1].OpCode != OpCodes.Ret)
        {
            _log.Debug("Adding final RET instruction");
            processor.Append(Instruction.Create(OpCodes.Ret));
        }

        _log.Debug($"Final instruction count: {targetMethod.Body.Instructions.Count}");
        _log.Info($"Successfully patched method: {targetMethod.FullName}");
    }

    public void PatchProperty(TypeDefinition targetType, PropertyDefinition plugProperty,
        string? targetPropertyName = null)
    {
        PropertyDefinition? targetProperty = targetType.Properties.FirstOrDefault(p => p.Name == targetPropertyName);
        if (targetProperty == null)
            return;

        _log.Info($"Patching property: {targetProperty.FullName}");

        if (plugProperty.GetMethod == null || plugProperty.SetMethod == null)
        {
            _log.Warn($"No {(plugProperty.GetMethod == null ? "get" : "set")} method in plug property");
            return;
        }

        (Instruction? Instruction, int Index) targetBackingField = FindField(targetProperty.GetMethod, string.Empty,
            OpCodes.Stfld, OpCodes.Stsfld, OpCodes.Ldfld, OpCodes.Ldsfld);

        (Instruction? Instruction, int Index) plugBackingField = FindField(plugProperty.GetMethod!, string.Empty,
            OpCodes.Stfld, OpCodes.Stsfld, OpCodes.Ldfld, OpCodes.Ldsfld);

        if (targetBackingField.Instruction == null || plugBackingField.Instruction == null)
            return;

        if (plugProperty.SetMethod != null)
        {
            _log.Debug("Patching set method");
            PatchMethod(targetProperty.SetMethod, plugProperty.SetMethod, true);
        }

        if (plugProperty.GetMethod != null)
        {
            _log.Debug("Patching get method");
            PatchMethod(targetProperty.GetMethod, plugProperty.GetMethod, true);
        }

        _log.Debug("Updating property attributes and type");
        targetProperty.Attributes = plugProperty.Attributes;
        targetProperty.PropertyType = targetProperty.Module.ImportReference(plugProperty.PropertyType);

        _log.Debug("Updating parameters");
        targetProperty.Parameters.Clear();
        foreach (ParameterDefinition param in plugProperty.Parameters)
        {
            ParameterDefinition importedParam = new(
                param.Name,
                param.Attributes,
                targetProperty.Module.ImportReference(param.ParameterType));
            targetProperty.Parameters.Add(importedParam);
        }

        targetProperty.Constant = plugProperty.Constant;
        targetProperty.HasConstant = plugProperty.HasConstant;
        targetProperty.Name = plugProperty.Name;

        FieldReference targetBackingFieldRef = (FieldReference)targetBackingField.Instruction.Operand;
        FieldReference plugBackingFieldRef = (FieldReference)plugBackingField.Instruction.Operand;

        PatchField(targetType, plugBackingFieldRef.Resolve(), targetBackingFieldRef.Name);
        if (targetProperty.SetMethod != null)
        {
            ReplaceFieldAccess(targetProperty.SetMethod, plugBackingFieldRef, targetBackingFieldRef);
        }

        if (targetProperty.GetMethod != null)
        {
            ReplaceFieldAccess(targetProperty.GetMethod, plugBackingFieldRef, targetBackingFieldRef, true);
        }

        _log.Info($"Completed property patch: {targetProperty.FullName}");
    }

    public void PatchField(TypeDefinition targetType, FieldDefinition plugField, string? targetFieldName = null)
    {
        FieldDefinition? targetField = targetType.Fields.FirstOrDefault(f => f.Name == targetFieldName);
        if (targetField == null)
            return;

        _log.Info($"Patching field: {targetField.FullName}");
        ModuleDefinition module = targetField.Module;

        targetField.FieldType = module.ImportReference(plugField.FieldType);
        _log.Debug($"Type patched: {plugField.FieldType.FullName}");

        targetField.Attributes = plugField.Attributes;
        _log.Debug($"Attributes patched: {plugField.Attributes}");

        if (plugField.HasConstant)
        {
            targetField.Constant = plugField.Constant;
            targetField.HasConstant = true;
            _log.Debug($"Constant value set: {plugField.Constant}");
        }

        if (plugField.InitialValue != null)
        {
            targetField.InitialValue = [.. plugField.InitialValue];
            _log.Debug("InitialValue set");
        }

        if (plugField.MarshalInfo != null)
        {
            targetField.MarshalInfo = plugField.MarshalInfo;
            _log.Debug("MarshalInfo copied");
        }

        foreach (MethodDefinition? targetCtor in targetField.DeclaringType.GetConstructors())
        {
            foreach (MethodDefinition? plugCtor in plugField.DeclaringType.GetConstructors())
            {
                (Instruction? Instruction, int Index) targetFieldInstr =
                    FindField(targetCtor, targetField, OpCodes.Stfld, OpCodes.Stsfld);
                (Instruction? Instruction, int Index) plugFieldInstr =
                    FindField(plugCtor, plugField, OpCodes.Stfld, OpCodes.Stsfld);

                if (targetFieldInstr.Instruction is null || plugFieldInstr.Instruction is null)
                {
                    //Console.WriteLine("[PatchField] Field instruction not found in one of the constructors.");
                    continue;
                }

                Instruction plugFieldValue = plugCtor.Body.Instructions[plugFieldInstr.Index - 1];
                Instruction clone = plugFieldValue.Clone();

                clone.Operand = plugFieldValue.Operand switch
                {
                    MethodReference m => module.ImportReference(m),
                    FieldReference f => module.ImportReference(f),
                    TypeReference t => module.ImportReference(t),
                    MemberReference mr => module.ImportReference(mr),
                    _ => plugFieldValue.Operand
                };

                ILProcessor processor = targetCtor.Body.GetILProcessor();
                processor.RemoveAt(targetFieldInstr.Index - 1); // previous index holds IL for the field value
                processor.InsertBefore(targetFieldInstr.Instruction, clone); // replace value with plugged value
            }
        }

        _log.Info($"Completed field patch: {targetField.FullName}");
    }

    private void ReplaceFieldAccess(MethodDefinition method, FieldReference oldField, FieldReference newField,
        bool loadField = false)
    {
        _log.Debug($"Processing method: {method.FullName}, Field: {oldField.FullName}, LoadField: {loadField}");

        ILProcessor processor = method.Body.GetILProcessor();
        Collection<Instruction> instructions = [.. method.Body.Instructions];
        foreach (Instruction instruction in instructions)
        {
            if (instruction.Operand is not FieldReference fieldRef || fieldRef.FullName != oldField.FullName)
                continue;

            _log.Debug($"Replacing instruction: {instruction} in method: {method.FullName}");

            OpCode opcode = method.IsStatic
                ? loadField ? OpCodes.Ldsfld : OpCodes.Stsfld
                : loadField
                    ? OpCodes.Ldfld
                    : OpCodes.Stfld;
            Instruction newInstruction = Instruction.Create(opcode, method.Module.ImportReference(newField));
            processor.Replace(instruction, newInstruction);

            _log.Debug($"Replaced: {instruction} -> {newInstruction}");
            _log.Debug($"Instruction in body: {method.Body.Instructions.IndexOf(newInstruction)}");
        }

        _log.Debug($"Completed processing for method: {method.FullName}");
    }

    private (Instruction? Instruction, int Index) FindField(MethodDefinition method, FieldDefinition fieldDefinition,
        params OpCode[] opcodes) => FindField(method, fieldDefinition.Name, opcodes);

    private (Instruction? Instruction, int Index) FindField(MethodDefinition method, string? name,
        params OpCode[] opcodes)
    {
        foreach (Instruction instruction in method.Body.Instructions)
        {
            if (!opcodes.Contains(instruction.OpCode) ||
                instruction.Operand is not FieldReference fr)
                continue;

            if (!string.IsNullOrEmpty(name) && fr.Resolve().Name != name)
                continue;

            int idx = method.Body.Instructions.IndexOf(instruction);
            return (instruction, idx);
        }

        return default;
    }

    private void RemoveMember(TypeDefinition targetType, IMemberDefinition member)
    {
        try
        {
            _ = member switch
            {
                MethodDefinition method => RemoveMethod(method),
                PropertyDefinition property => RemoveProperty(property),
                FieldDefinition field => targetType.Fields.Remove(field),
                _ => throw new InvalidOperationException("Unsupported member type")
            };

            _log.Debug($"Removed member: {member.Name}");

            bool RemoveMethod(MethodDefinition method)
            {
                method.Body?.Clear();
                method.CustomAttributes.Clear();
                method.Body = null;


                targetType.Methods.Remove(method);
                return true;
            }

            bool RemoveProperty(PropertyDefinition property)
            {
                FieldDefinition? backingField = null;
                if (property.GetMethod != null)
                {
                    (Instruction? Instruction, int Index) fieldInfo = FindField(property.GetMethod, string.Empty,
                        OpCodes.Stfld, OpCodes.Stsfld, OpCodes.Ldfld, OpCodes.Ldsfld);
                    if (fieldInfo.Instruction != null)
                        backingField = ((FieldReference)fieldInfo.Instruction.Operand).Resolve();

                    RemoveMethod(property.GetMethod);
                }

                if (property.SetMethod != null)
                    RemoveMethod(property.SetMethod);


                if (backingField != null)
                    targetType.Fields.Remove(backingField);

                targetType.Properties.Remove(property);
                _log.Debug($"Removed property: {property.Name}");
                return true;
            }
        }
        catch (Exception ex)
        {
            _log.Error($"ERROR removing member {member.Name}: {ex}");
            throw;
        }
    }


    /// <summary>
    /// Fix branch instruction targets to point to cloned instructions instead of original source instructions.
    /// </summary>
    private static void FixBranchTargets(Dictionary<Instruction, Instruction> instructionMap)
    {
        foreach (var kvp in instructionMap)
        {
            Instruction clone = kvp.Value;
            if (clone.Operand is Instruction branchTarget && instructionMap.TryGetValue(branchTarget, out Instruction? mappedTarget))
            {
                clone.Operand = mappedTarget;
            }
            else if (clone.Operand is Instruction[] switchTargets)
            {
                for (int i = 0; i < switchTargets.Length; i++)
                {
                    if (instructionMap.TryGetValue(switchTargets[i], out Instruction? mapped))
                    {
                        switchTargets[i] = mapped;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Remaps a parameter reference from the plug method to the target method.
    /// For instance plugs (static plug with aThis parameter targeting an instance method):
    /// - plug param[0] (aThis) maps to target's implicit 'this' (arg 0, not a parameter)
    /// - plug param[N] (N > 0) maps to target param[N-1]
    /// </summary>
    private static object RemapParameter(ParameterDefinition plugParam, MethodDefinition plugMethod,
        MethodDefinition targetMethod, bool isInstancePlug)
    {
        int plugIndex = plugParam.Index;

        if (isInstancePlug && !targetMethod.IsStatic)
        {
            if (plugIndex == 0)
            {
                // aThis parameter becomes target's implicit 'this' (arg 0)
                return (sbyte)0;
            }
            else
            {
                // Map plug param[N] to target param[N-1]
                int targetParamIndex = plugIndex - 1;
                if (targetParamIndex >= 0 && targetParamIndex < targetMethod.Parameters.Count)
                {
                    return targetMethod.Parameters[targetParamIndex];
                }
            }
        }
        else
        {
            // Static plug to static target: direct mapping
            if (plugIndex >= 0 && plugIndex < targetMethod.Parameters.Count)
            {
                return targetMethod.Parameters[plugIndex];
            }
        }

        return plugParam;
    }

    // ---------- SAFE IMPORT HELPERS (fix self-references) ----------

    /// <summary>
    /// Imports a type reference, fixing self-references to use TypeDef instead of TypeRef.
    /// Prevents "Invalid TypeRef token" errors when a patched assembly references itself.
    /// </summary>
    private static TypeReference SafeImportType(ModuleDefinition module, TypeReference typeRef)
    {
        if (typeRef == null) return null;

        var imported = module.ImportReference(typeRef);

        if (!MightHaveSelfReference(module, imported))
            return imported;

        return FixSelfReferences(module, imported);
    }

    /// <summary>
    /// Imports a method reference, fixing self-references in declaring type and parameters.
    /// </summary>
    private static MethodReference SafeImportMethod(ModuleDefinition module, MethodReference methodRef)
    {
        if (methodRef == null) return null;

        var imported = module.ImportReference(methodRef);

        bool needsNewRef = false;
        var fixedDeclaringType = imported.DeclaringType;
        var fixedReturnType = imported.ReturnType;
        var fixedParams = new List<(string Name, ParameterAttributes Attrs, TypeReference Type)>();

        if (imported.DeclaringType != null && MightHaveSelfReference(module, imported.DeclaringType))
        {
            var fixed_ = FixSelfReferences(module, imported.DeclaringType);
            if (fixed_ != imported.DeclaringType)
            {
                fixedDeclaringType = fixed_;
                needsNewRef = true;
            }
        }

        if (imported.ReturnType != null && MightHaveSelfReference(module, imported.ReturnType))
        {
            var fixed_ = FixSelfReferences(module, imported.ReturnType);
            if (fixed_ != imported.ReturnType)
            {
                fixedReturnType = fixed_;
                needsNewRef = true;
            }
        }

        foreach (var param in imported.Parameters)
        {
            if (param.ParameterType != null && MightHaveSelfReference(module, param.ParameterType))
            {
                var fixed_ = FixSelfReferences(module, param.ParameterType);
                fixedParams.Add((param.Name, param.Attributes, fixed_));
                if (fixed_ != param.ParameterType)
                    needsNewRef = true;
            }
            else
            {
                fixedParams.Add((param.Name, param.Attributes, param.ParameterType));
            }
        }

        if (needsNewRef)
        {
            if (fixedDeclaringType is TypeDefinition typeDef)
            {
                var methodDef = typeDef.Methods.FirstOrDefault(m =>
                    m.Name == imported.Name &&
                    m.Parameters.Count == fixedParams.Count &&
                    m.Parameters.Select(p => p.ParameterType.FullName)
                        .SequenceEqual(fixedParams.Select(p => p.Type.FullName)));

                if (methodDef != null)
                    return methodDef;
            }

            var newMethodRef = new MethodReference(imported.Name, fixedReturnType, fixedDeclaringType)
            {
                HasThis = imported.HasThis,
                ExplicitThis = imported.ExplicitThis,
                CallingConvention = imported.CallingConvention
            };

            foreach (var (name, attrs, type) in fixedParams)
                newMethodRef.Parameters.Add(new ParameterDefinition(name, attrs, type));

            foreach (var gp in imported.GenericParameters)
                newMethodRef.GenericParameters.Add(new GenericParameter(gp.Name, newMethodRef));

            return newMethodRef;
        }

        return imported;
    }

    /// <summary>
    /// Imports a field reference, fixing self-references in declaring type and field type.
    /// </summary>
    private static FieldReference SafeImportField(ModuleDefinition module, FieldReference fieldRef)
    {
        if (fieldRef == null) return null;

        var imported = module.ImportReference(fieldRef);

        bool needsNewRef = false;
        var fixedDeclaringType = imported.DeclaringType;
        var fixedFieldType = imported.FieldType;

        if (imported.DeclaringType != null && MightHaveSelfReference(module, imported.DeclaringType))
        {
            var fixed_ = FixSelfReferences(module, imported.DeclaringType);
            if (fixed_ != imported.DeclaringType)
            {
                fixedDeclaringType = fixed_;
                needsNewRef = true;
            }
        }

        if (imported.FieldType != null && MightHaveSelfReference(module, imported.FieldType))
        {
            var fixed_ = FixSelfReferences(module, imported.FieldType);
            if (fixed_ != imported.FieldType)
            {
                fixedFieldType = fixed_;
                needsNewRef = true;
            }
        }

        if (needsNewRef)
            return new FieldReference(imported.Name, fixedFieldType, fixedDeclaringType);

        return imported;
    }

    private static bool MightHaveSelfReference(ModuleDefinition module, TypeReference typeRef)
    {
        if (typeRef == null) return false;

        string targetAsmName = module.Assembly.Name.Name;

        if (typeRef.Scope is AssemblyNameReference asmRef && asmRef.Name == targetAsmName)
            return true;

        if (typeRef is GenericInstanceType git)
        {
            foreach (var arg in git.GenericArguments)
                if (MightHaveSelfReference(module, arg))
                    return true;
        }

        if (typeRef is TypeSpecification typeSpec)
            return MightHaveSelfReference(module, typeSpec.ElementType);

        return false;
    }

    private static TypeReference FixSelfReferences(ModuleDefinition module, TypeReference typeRef)
    {
        if (typeRef == null) return null;

        if (typeRef is GenericInstanceType git)
        {
            bool needsFix = false;
            var fixedArgs = new List<TypeReference>();

            foreach (var arg in git.GenericArguments)
            {
                var fixedArg = FixSelfReferences(module, arg);
                fixedArgs.Add(fixedArg);
                if (fixedArg != arg)
                    needsFix = true;
            }

            if (needsFix)
            {
                var result = new GenericInstanceType(git.ElementType);
                foreach (var arg in fixedArgs)
                    result.GenericArguments.Add(arg);
                return result;
            }
            return git;
        }

        if (typeRef is ArrayType arrayType)
        {
            var fixedElement = FixSelfReferences(module, arrayType.ElementType);
            if (fixedElement != arrayType.ElementType)
                return new ArrayType(fixedElement, arrayType.Rank);
            return arrayType;
        }

        if (typeRef is ByReferenceType byRefType)
        {
            var fixedElement = FixSelfReferences(module, byRefType.ElementType);
            if (fixedElement != byRefType.ElementType)
                return new ByReferenceType(fixedElement);
            return byRefType;
        }

        if (typeRef is PointerType ptrType)
        {
            var fixedElement = FixSelfReferences(module, ptrType.ElementType);
            if (fixedElement != ptrType.ElementType)
                return new PointerType(fixedElement);
            return ptrType;
        }

        if (typeRef is GenericParameter)
            return typeRef;

        if (typeRef.Scope is AssemblyNameReference asmRef && asmRef.Name == module.Assembly.Name.Name)
        {
            var typeDef = module.GetType(typeRef.FullName);
            if (typeDef != null)
                return typeDef;
        }

        return typeRef;
    }

    // ---------- DEBUG HELPERS ----------
    private static string FmtParams(IEnumerable<ParameterDefinition> ps)
        => "(" + string.Join(", ", ps.Select(p => p.ParameterType?.FullName ?? "?")) + ")";

    private static string FmtMethod(MethodDefinition m)
    {
        string owner = m.DeclaringType?.FullName ?? "?";
        string ret = m.ReturnType?.FullName ?? "void";
        string name = m.IsConstructor ? (m.IsStatic ? ".cctor" : ".ctor") : m.Name;
        string inst = m.IsStatic ? "static" : "instance";
        return $"{inst} {ret} {owner}::{name}{FmtParams(m.Parameters)}";
    }


    /// <summary>
    /// List all overloads on the target type for a given name and annotate match on params count and instance/static.
    /// </summary>
    private void DumpOverloads(TypeDefinition targetType, string methodName, IMemberDefinition plug)
    {
        if (plug is not MethodDefinition methodPlug)
            return;
        bool isInstancePlug = methodPlug.Parameters.Any(p => p.Name == "aThis");
        int expectedCount = methodPlug.Parameters.Count - (isInstancePlug ? 1 : 0);
        _log.Debug($"In type: {targetType.FullName}, name: {methodName}");

        MethodDefinition[] overloads = [.. targetType.Methods.Where(m => m.Name == methodName)];
        if (overloads.Length == 0)
        {
            _log.Debug("none");
            return;
        }

        foreach (MethodDefinition? m in overloads)
        {
            bool countOk = m.Parameters.Count == expectedCount;
            bool instOk = !isInstancePlug || !m.IsStatic; // instance plug expects instance target
            _log.Debug(
                $"  - {FmtMethod(m)}  [params:{m.Parameters.Count} {(countOk ? "OK" : "NO")}, instance:{(!m.IsStatic)} {(instOk ? "OK" : "NO")}]");
        }
    }

    private void DumpIL(MethodDefinition method)
    {
        _log.Debug($"IL for method: {method.FullName}");
        foreach (Instruction instruction in method.Body.Instructions)
        {
            _log.Debug($"  {instruction}");
        }
    }
}
