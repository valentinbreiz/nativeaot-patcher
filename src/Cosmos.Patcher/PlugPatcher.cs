using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Cosmos.Build.API.Attributes;
using Cosmos.Patcher.Logging;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Mono.Collections.Generic;
using MonoMod.Cil;
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
        _log.Debug("[Init] Initializing PlugPatcher...");
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _log.Debug($"[Init] PlugPatcher initialized with scanner: {scanner.GetType().FullName}");
        MonoCecilExtensions.Logger = _log;
    }

    public void PatchAssembly(AssemblyDefinition targetAssembly, params AssemblyDefinition[] plugAssemblies)
    {
        _log.Info($"[PatchAssembly] Starting patch process for assembly: {targetAssembly.FullName}");
        _log.Debug($"[PatchAssembly] Plug assemblies provided: {plugAssemblies.Length}");

        try
        {
            List<TypeDefinition> allPlugs = _scanner.LoadPlugs(plugAssemblies);
            Dictionary<string, List<TypeDefinition>> plugsByTarget = new();

            foreach (TypeDefinition plug in allPlugs)
            {
                string? targetName = GetPlugTargetName(plug);
                if (string.IsNullOrWhiteSpace(targetName))
                    continue;

                if (!plugsByTarget.TryGetValue(targetName, out List<TypeDefinition>? list))
                {
                    list = [];
                    plugsByTarget[targetName] = list;
                }
                list.Add(plug);
            }

            if (plugsByTarget.Count == 0)
            {
                _log.Info("[PatchAssembly] No plugs found for this assembly. Skipping patching.");
                return;
            }

            foreach ((string targetName, List<TypeDefinition> plugTypes) in plugsByTarget)
            {
                TypeDefinition? targetType = targetAssembly.MainModule.GetType(targetName)
                    ?? targetAssembly.MainModule.Types.FirstOrDefault(t => t.FullName == targetName);

                if (targetType == null)
                {
                    _log.Warn($"[PatchAssembly] Target type not found: {targetName}");
                    continue;
                }

                if (targetType.CustomAttributes.Any(attr => attr.AttributeType.FullName == typeof(PlugAttribute).FullName))
                {
                    _log.Info($"[PatchAssembly] Skipping type marked with PlugAttribute: {targetType.FullName}");
                    continue;
                }

                _log.Info($"[PatchAssembly] Processing type: {targetType.FullName}");

                foreach (TypeDefinition plugType in plugTypes)
                {
                    try
                    {
                        ProcessPlugMembers(targetType, plugType);
                        _log.Debug($"[PatchAssembly] Successfully processed plug {plugType.FullName} for type {targetType.FullName}");
                    }
                    catch (Exception ex)
                    {
                        _log.Error($"[PatchAssembly] ERROR processing type {targetType.FullName} with plug {plugType.FullName}: {ex}");
                    }
                }
            }

            _log.Debug("[PatchAssembly] Updating fields, properties, and methods...");
            targetAssembly.UpdateFieldsPropertiesAndMethods(true);
            _log.Info($"[PatchAssembly] Assembly {targetAssembly.Name} updated successfully");
        }
        catch (Exception ex)
        {
            _log.Error($"[PatchAssembly] CRITICAL ERROR patching assembly: {ex}");
            throw;
        }

        _log.Info($"[PatchAssembly] Completed patching assembly: {targetAssembly.FullName}");
    }

    private static string? GetPlugTargetName(TypeDefinition plugType)
    {
        CustomAttribute? plugAttr = plugType.CustomAttributes
            .FirstOrDefault(attr => attr.AttributeType.FullName == typeof(PlugAttribute).FullName);
        if (plugAttr == null)
            return null;

        return plugAttr.ConstructorArguments.Count == 1 && plugAttr.Properties.Count == 0
            ? plugAttr.GetArgument<string>()
            : plugAttr.GetArgument<string>(named: "Target") ?? plugAttr.GetArgument<string>(named: "TargetName");
    }

    public void PatchType(TypeDefinition targetType, params AssemblyDefinition[] plugAssemblies)
    {
        _log.Info($"[PatchType] Starting patch process for type: {targetType.FullName}");
        if (plugAssemblies.Length == 0)
        {
            _log.Error("[PatchType] ERROR: No plug assemblies provided");
            return;
        }

        if (targetType.CustomAttributes.Any(attr => attr.AttributeType.FullName == typeof(PlugAttribute).FullName))
        {
            _log.Info($"[PatchType] Skipping type marked with PlugAttribute: {targetType.FullName}");
            return;
        }

        _log.Debug("[PatchType] Loading plugs from provided assemblies...");
        List<TypeDefinition> plugs = _scanner.LoadPlugs(targetType, plugAssemblies);
        _log.Info($"[PatchType] Found {plugs.Count} plug types to process");
        if (plugs.Count == 0)
            return;

        foreach (TypeDefinition plugType in plugs)
        {
            _log.Info($"[PatchType] Processing members for plug type: {plugType.FullName}");
            ProcessPlugMembers(targetType, plugType);
        }

        _log.Info($"[PatchType] Completed processing type: {targetType.FullName}");
    }

    private void ProcessPlugMembers(TypeDefinition targetType, TypeDefinition plugType)
    {
        _log.Debug($"[ProcessPlugMembers] Processing methods for {plugType.FullName}");
        foreach (MethodDefinition method in plugType.Methods.Where(m => !m.IsConstructor))
        {
            _log.Debug($"[ProcessPlugMembers] Processing method: {method.FullName}");
            CustomAttribute plugMemberAttr = method.CustomAttributes.FirstOrDefault(attr =>
                attr.AttributeType.FullName == typeof(PlugMemberAttribute).FullName);

            if (plugMemberAttr == null)
            {
                _log.Debug($"[ProcessPlugMembers] Skipping method without PlugMemberAttribute: {method.Name}");
                continue;
            }

            try
            {
                plugMemberAttr.ImportReferences(targetType.Module);
                ResolveAndPatchMethod(targetType, method, plugMemberAttr);
            }
            catch (Exception ex)
            {
                _log.Error($"[ProcessPlugMembers] ERROR processing method {method.FullName}: {ex}");
                // --- DEBUG DUMP ---
                _log.Debug("[ProcessPlugMembers] --- DEBUG DUMP BEGIN ---");
                _log.Debug($"[Plug]   {FmtMethod(method)}");
                try
                {
                    string targetName = TryGetNamedString(plugMemberAttr, "TargetName") ?? method.Name;
                    _log.Debug($"[Attr]   PlugMember.TargetName = {targetName}");
                    DumpOverloads(targetType, targetName, method);
                }
                catch (Exception e2)
                {
                    _log.Warn($"[ProcessPlugMembers] WARN while dumping overloads: {e2}");
                }
                _log.Debug("[ProcessPlugMembers] --- DEBUG DUMP END ---");
            }
        }

        _log.Debug($"[ProcessPlugMembers] Processing properties for {plugType.FullName}");
        foreach (PropertyDefinition property in plugType.Properties)
        {
            _log.Debug($"[ProcessPlugMembers] Processing property: {property.FullName}");
            CustomAttribute? plugMemberAttr = property.CustomAttributes.FirstOrDefault(attr =>
                attr.AttributeType.FullName == typeof(PlugMemberAttribute).FullName);

            if (plugMemberAttr == null)
            {
                _log.Debug($"[ProcessPlugMembers] Skipping property without PlugMemberAttribute: {property.Name}");
                continue;
            }

            string targetPropertyName = TryGetNamedString(plugMemberAttr, "TargetName") ?? property.Name;
            _log.Debug($"[ProcessPlugMembers] Looking for target property: {targetPropertyName}");
            PropertyDefinition? targetProperty =
                targetType.Properties.FirstOrDefault(p => p.Name == targetPropertyName);

            if (targetProperty == null)
            {
                _log.Warn($"[ProcessPlugMembers] Target property not found: {targetPropertyName}");
                continue;
            }

            try
            {
                PatchProperty(targetProperty, property);
                _log.Info($"[ProcessPlugMembers] Successfully patched property: {targetProperty.Name}");
            }
            catch (Exception ex)
            {
                _log.Error($"[ProcessPlugMembers] ERROR patching property {targetPropertyName}: {ex}");
            }
        }

        _log.Debug($"[ProcessPlugMembers] Processing fields for {plugType.FullName}");
        foreach (FieldDefinition field in plugType.Fields)
        {
            _log.Debug($"[ProcessPlugMembers] Processing field: {field.FullName}");
            CustomAttribute? plugMemberAttr = field.CustomAttributes.FirstOrDefault(attr =>
                attr.AttributeType.FullName == typeof(PlugMemberAttribute).FullName);

            if (plugMemberAttr == null)
            {
                _log.Debug($"[ProcessPlugMembers] Skipping field without PlugMemberAttribute: {field.Name}");
                continue;
            }

            string targetFieldName = TryGetNamedString(plugMemberAttr, "TargetName") ?? field.Name;
            _log.Debug($"[ProcessPlugMembers] Looking for target field: {targetFieldName}");
            FieldDefinition? targetField = targetType.Fields.FirstOrDefault(f =>
            {
                _log.Debug($"[ProcessPlugMembers] Target has field: {f.FullName}");
                return f.Name == targetFieldName;
            });

            if (targetField == null)
            {
                _log.Warn($"[ProcessPlugMembers] Target field not found: {targetFieldName}");
                continue;
            }

            try
            {
                PatchField(targetField, field);
                _log.Info($"[ProcessPlugMembers] Successfully patched field: {targetField.Name}");
            }
            catch (Exception ex)
            {
                _log.Error($"[ProcessPlugMembers] ERROR patching field {targetFieldName}: {ex}");
            }
        }
    }

    private void ResolveAndPatchMethod(TypeDefinition targetType, MethodDefinition plugMethod, CustomAttribute attr)
    {
        _log.Debug($"[ResolveAndPatchMethod] Starting method resolution for {plugMethod.FullName}");

        if (plugMethod.Name is "Ctor" or "CCtor")
        {
            bool isInstanceCtor = plugMethod.Parameters.Any(p => p.Name == "aThis");
            _log.Debug($"[ResolveAndPatchMethod] Handling constructor plug (Instance: {isInstanceCtor})");

            MethodDefinition? ctor = targetType.Methods.FirstOrDefault(m =>
                m.IsConstructor &&
                (isInstanceCtor
                    ? m.Parameters.Count + 1 == plugMethod.Parameters.Count
                    : m.Parameters.Count == plugMethod.Parameters.Count) &&
                (plugMethod.Name != "CCtor" || m.IsStatic));

            if (ctor != null)
            {
                _log.Debug($"[ResolveAndPatchMethod] Found target constructor: {ctor.FullName}");
                _log.Debug($"[ResolveAndPatchMethod] Target prototype: {FmtMethod(ctor)}");
                _log.Debug($"[ResolveAndPatchMethod] Plug   prototype: {FmtMethod(plugMethod)}");
                PatchMethod(ctor, plugMethod);
            }
            else
            {
                _log.Warn($"[ResolveAndPatchMethod] No matching constructor found for {plugMethod.FullName}");
                _log.Debug($"[ResolveAndPatchMethod] Plug parameters: {string.Join(", ", plugMethod.Parameters.Select(p => p.ParameterType + " " + p.Name))}");
            }

            return;
        }

        bool isInstancePlug = plugMethod.Parameters.Any(p => p.Name == "aThis");
        string methodName = TryGetNamedString(attr, "TargetName") ?? plugMethod.Name;
        _log.Debug($"[ResolveAndPatchMethod] Resolving method: {methodName} (Instance: {isInstancePlug})");
        _log.Debug($"[ResolveAndPatchMethod] Plug prototype: {FmtMethod(plugMethod)}");

        // Find method with matching name, parameter count, AND parameter types
        MethodDefinition? targetMethod = null;

        foreach (var m in targetType.Methods.Where(m => m.Name == methodName))
        {
            // Check parameter count
            int expectedParamCount = isInstancePlug ? plugMethod.Parameters.Count - 1 : plugMethod.Parameters.Count;
            if (m.Parameters.Count != expectedParamCount)
                continue;

            // Check parameter types match
            bool typesMatch = true;
            int plugStartIndex = isInstancePlug ? 1 : 0; // Skip 'aThis' parameter if instance plug

            for (int i = 0; i < m.Parameters.Count; i++)
            {
                var targetParam = m.Parameters[i];
                var plugParam = plugMethod.Parameters[i + plugStartIndex];

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
            _log.Debug($"[ResolveAndPatchMethod] Found target method: {targetMethod.FullName}");
            _log.Debug($"[ResolveAndPatchMethod] Parameters: {targetMethod.Parameters.Count}");
            _log.Debug($"[ResolveAndPatchMethod] Target prototype: {FmtMethod(targetMethod)}");
            PatchMethod(targetMethod, plugMethod);
        }
        else
        {
            _log.Warn($"[ResolveAndPatchMethod] Target method not found: {methodName}");
            _log.Debug($"[ResolveAndPatchMethod] Expected parameters: {plugMethod.Parameters.Count - (isInstancePlug ? 1 : 0)}");
            _log.Debug($"[ResolveAndPatchMethod] Expected parameter types: {string.Join(", ", plugMethod.Parameters.Skip(isInstancePlug ? 1 : 0).Select(p => p.ParameterType.FullName))}");
            DumpOverloads(targetType, methodName, plugMethod);
        }
    }

    public void PatchMethod(MethodDefinition targetMethod, MethodDefinition plugMethod, bool instance = false)
    {
        _log.Info($"[PatchMethod] Starting method patch: {targetMethod.FullName} <- {plugMethod.FullName}");
        _log.Debug($"[PatchMethod] Target method attributes: Static={targetMethod.IsStatic}, Constructor={targetMethod.IsConstructor}");
        _log.Debug($"[PatchMethod] Plug method parameters: {plugMethod.Parameters.Count}");

        targetMethod.Body ??= new MethodBody(targetMethod);
        ILProcessor processor = targetMethod.Body.GetILProcessor();
        _log.Debug($"[PatchMethod] Original instruction count: {targetMethod.Body.Instructions.Count}");

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
                _log.Debug($"[PatchMethod] Base Constructor Call found at index {index}, preserving {instructionsToKeep} instructions");
                while (targetMethod.Body.Instructions.Count > instructionsToKeep)
                {
                    processor.RemoveAt(instructionsToKeep);
                }
            }
            else
            {
                _log.Debug("[PatchMethod] No Base Constructor Call found in constructor, clearing all instructions");
                targetMethod.Body.Instructions.Clear();
            }
        }
        else
        {
            _log.Debug("[PatchMethod] Clearing non-constructor method body");
            targetMethod.Body.Instructions.Clear();
        }

        bool isInstance = plugMethod.Parameters.Any(p => p.Name == "aThis") || instance;
        _log.Debug($"[PatchMethod] Instance method: {isInstance}, Target static: {targetMethod.IsStatic}");

        if ((isInstance && !targetMethod.IsStatic) || targetMethod.IsConstructor)
        {
            _log.Debug($"[PatchMethod] Cloning {plugMethod.Body.Instructions.Count} instructions");
            foreach (Instruction instruction in plugMethod.Body.Instructions)
            {
                try
                {
                    Instruction clone = instruction.Clone();
                    clone.Operand = instruction.Operand switch
                    {
                        MethodReference m => targetMethod.Module.ImportReference(m),
                        FieldReference f => targetMethod.Module.ImportReference(f),
                        TypeReference t => targetMethod.Module.ImportReference(t),
                        MemberReference mr => targetMethod.Module.ImportReference(mr),
                        _ => instruction.Operand
                    };
                    processor.Append(clone);
                    _log.Debug($"[PatchMethod] Cloned instruction {instruction} -> {clone}");
                }
                catch (Exception ex)
                {
                    _log.Error($"[PatchMethod] ERROR cloning instruction {instruction}: {ex}");
                    throw;
                }
            }
        }
        else
        {
            _log.Debug("[PatchMethod] Performing full method swap with proper imports");
            try
            {
                // Clear existing body completely
                targetMethod.Body.Variables.Clear();
                targetMethod.Body.ExceptionHandlers.Clear();

                // Copy variables with imported types
                foreach (var variable in plugMethod.Body.Variables)
                {
                    targetMethod.Body.Variables.Add(
                        new VariableDefinition(targetMethod.Module.ImportReference(variable.VariableType))
                    );
                }

                // Clone and import all instructions
                foreach (Instruction instruction in plugMethod.Body.Instructions)
                {
                    Instruction clone = instruction.Clone();

                    // Import all member references - this is the critical part
                    clone.Operand = instruction.Operand switch
                    {
                        MethodReference m => targetMethod.Module.ImportReference(m),
                        FieldReference f => targetMethod.Module.ImportReference(f),
                        TypeReference t => targetMethod.Module.ImportReference(t),
                        MemberReference mr => targetMethod.Module.ImportReference(mr),
                        _ => instruction.Operand
                    };

                    processor.Append(clone);
                    _log.Debug($"[PatchMethod] Cloned instruction {instruction} -> {clone}");
                }

                // Copy exception handlers if any
                if (plugMethod.Body.HasExceptionHandlers)
                {
                    foreach (var handler in plugMethod.Body.ExceptionHandlers)
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
                            newHandler.CatchType = targetMethod.Module.ImportReference(handler.CatchType);
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
            }
            catch (Exception ex)
            {
                _log.Error($"[PatchMethod] ERROR during method swap: {ex}");
                throw;
            }
        }

        // Remove P/Invoke metadata so the runtime treats the method as managed
        if (targetMethod.IsPInvokeImpl || targetMethod.PInvokeInfo is not null)
        {
            _log.Debug("[PatchMethod] Removing P/Invoke metadata");
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
            _log.Debug("[PatchMethod] Adding final RET instruction");
            processor.Append(Instruction.Create(OpCodes.Ret));
        }

        _log.Debug($"[PatchMethod] Final instruction count: {targetMethod.Body.Instructions.Count}");
        _log.Info($"[PatchMethod] Successfully patched method: {targetMethod.FullName}");
    }

    public void PatchProperty(PropertyDefinition targetProperty, PropertyDefinition plugProperty)
    {
        _log.Info($"[PatchProperty] Patching property: {targetProperty.FullName}");

        if (plugProperty.GetMethod == null || plugProperty.SetMethod == null)
        {
            _log.Warn($"[PatchProperty] No {(plugProperty.GetMethod == null ? "get" : "set")} method in plug property");
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
            _log.Debug("[PatchProperty] Patching set method");
            PatchMethod(targetProperty.SetMethod, plugProperty.SetMethod, true);
        }

        if (plugProperty.GetMethod != null)
        {
            _log.Debug("[PatchProperty] Patching get method");
            PatchMethod(targetProperty.GetMethod, plugProperty.GetMethod, true);
        }

        _log.Debug("[PatchProperty] Updating property attributes and type");
        targetProperty.Attributes = plugProperty.Attributes;
        targetProperty.PropertyType = targetProperty.Module.ImportReference(plugProperty.PropertyType);

        _log.Debug("[PatchProperty] Updating parameters");
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

        PatchField(targetBackingFieldRef.Resolve(), plugBackingFieldRef.Resolve());
        if (targetProperty.SetMethod != null)
        {
            ReplaceFieldAccess(targetProperty.SetMethod, plugBackingFieldRef, targetBackingFieldRef);
        }

        if (targetProperty.GetMethod != null)
        {
            ReplaceFieldAccess(targetProperty.GetMethod, plugBackingFieldRef, targetBackingFieldRef, true);
        }

        _log.Info($"[PatchProperty] Completed property patch: {targetProperty.FullName}");
    }

    public void PatchField(FieldDefinition targetField, FieldDefinition plugField)
    {
        _log.Info($"[PatchField] Patching field: {targetField.FullName}");
        ModuleDefinition module = targetField.Module;

        targetField.FieldType = module.ImportReference(plugField.FieldType);
        _log.Debug($"[PatchField] Type patched: {plugField.FieldType.FullName}");

        targetField.Attributes = plugField.Attributes;
        _log.Debug($"[PatchField] Attributes patched: {plugField.Attributes}");

        if (plugField.HasConstant)
        {
            targetField.Constant = plugField.Constant;
            targetField.HasConstant = true;
            _log.Debug($"[PatchField] Constant value set: {plugField.Constant}");
        }

        if (plugField.InitialValue != null)
        {
            targetField.InitialValue = [.. plugField.InitialValue];
            _log.Debug("[PatchField] InitialValue set");
        }

        if (plugField.MarshalInfo != null)
        {
            targetField.MarshalInfo = plugField.MarshalInfo;
            _log.Debug("[PatchField] MarshalInfo copied");
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

        _log.Info($"[PatchField] Completed field patch: {targetField.FullName}");
    }

    private void ReplaceFieldAccess(MethodDefinition method, FieldReference oldField, FieldReference newField,
        bool loadField = false)
    {
        _log.Debug($"[ReplaceFieldAccess] Processing method: {method.FullName}, Field: {oldField.FullName}, LoadField: {loadField}");

        ILProcessor processor = method.Body.GetILProcessor();
        Collection<Instruction> instructions = [.. method.Body.Instructions];
        foreach (Instruction instruction in instructions)
        {
            if (instruction.Operand is not FieldReference fieldRef || fieldRef.FullName != oldField.FullName)
                continue;

            _log.Debug($"[ReplaceFieldAccess] Replacing instruction: {instruction} in method: {method.FullName}");

            OpCode opcode = method.IsStatic
                ? loadField ? OpCodes.Ldsfld : OpCodes.Stsfld
                : loadField ? OpCodes.Ldfld : OpCodes.Stfld;
            Instruction newInstruction = Instruction.Create(opcode, method.Module.ImportReference(newField));
            processor.Replace(instruction, newInstruction);

            _log.Debug($"[ReplaceFieldAccess] Replaced: {instruction} -> {newInstruction}");
            _log.Debug($"[ReplaceFieldAccess] Instruction in body: {method.Body.Instructions.IndexOf(newInstruction)}");
        }

        _log.Debug($"[ReplaceFieldAccess] Completed processing for method: {method.FullName}");
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

    private static string? TryGetNamedString(CustomAttribute attr, string propertyName)
    {
        try
        {
            if (attr.HasProperties)
            {
                foreach (var p in attr.Properties)
                    if (p.Name == propertyName && p.Argument.Value is string s)
                        return s;
            }
        }
        catch
        {
            // best-effort debug
        }
        return null;
    }

    /// <summary>
    /// List all overloads on the target type for a given name and annotate match on params count and instance/static.
    /// </summary>
    private void DumpOverloads(TypeDefinition targetType, string methodName, MethodDefinition plug)
    {
        bool isInstancePlug = plug.Parameters.Any(p => p.Name == "aThis");
        int expectedCount = plug.Parameters.Count - (isInstancePlug ? 1 : 0);
        _log.Debug($"[Overloads] In type: {targetType.FullName}, name: {methodName}");

        var overloads = targetType.Methods.Where(m => m.Name == methodName).ToArray();
        if (overloads.Length == 0)
        {
            _log.Debug("[Overloads] none");
            return;
        }

        foreach (var m in overloads)
        {
            bool countOk = m.Parameters.Count == expectedCount;
            bool instOk = isInstancePlug ? !m.IsStatic : true; // instance plug expects instance target
            _log.Debug($"  - {FmtMethod(m)}  [params:{m.Parameters.Count} {(countOk ? "OK" : "NO")}, instance:{(!m.IsStatic)} {(instOk ? "OK" : "NO")}]");
        }
    }
}
