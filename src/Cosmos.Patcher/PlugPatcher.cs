using System.Xml.Linq;
using Cosmos.Build.API.Attributes;
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

    public PlugPatcher(PlugScanner scanner)
    {
        Console.WriteLine("[Init] Initializing PlugPatcher...");
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        Console.WriteLine($"[Init] PlugPatcher initialized with scanner: {scanner.GetType().FullName}");
    }

    public void PatchAssembly(AssemblyDefinition targetAssembly, params AssemblyDefinition[] plugAssemblies)
    {
        Console.WriteLine($"[PatchAssembly] Starting patch process for assembly: {targetAssembly.FullName}");
        Console.WriteLine($"[PatchAssembly] Plug assemblies provided: {plugAssemblies.Length}");

        try
        {
            foreach (TypeDefinition type in targetAssembly.MainModule.Types)
            {
                Console.WriteLine($"[PatchAssembly] Processing type: {type.FullName}");
                try
                {
                    PatchType(type, plugAssemblies);
                    Console.WriteLine($"[PatchAssembly] Successfully processed type: {type.FullName}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PatchAssembly] ERROR processing type {type.FullName}: {ex}");
                }
            }

            Console.WriteLine("[PatchAssembly] Updating fields, properties, and methods...");
            targetAssembly.UpdateFieldsPropertiesAndMethods(true);
            Console.WriteLine($"[PatchAssembly] Assembly {targetAssembly.Name} updated successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PatchAssembly] CRITICAL ERROR patching assembly: {ex}");
            throw;
        }

        Console.WriteLine($"[PatchAssembly] Completed patching assembly: {targetAssembly.FullName}");
    }

    public void PatchType(TypeDefinition targetType, params AssemblyDefinition[] plugAssemblies)
    {
        Console.WriteLine($"[PatchType] Starting patch process for type: {targetType.FullName}");
        if (plugAssemblies.Length == 0)
        {
            Console.WriteLine("[PatchType] ERROR: No plug assemblies provided");
            return;
        }

        if (targetType.CustomAttributes.Any(attr => attr.AttributeType.FullName == typeof(PlugAttribute).FullName))
        {
            Console.WriteLine($"[PatchType] Skipping type marked with PlugAttribute: {targetType.FullName}");
            return;
        }

        Console.WriteLine("[PatchType] Loading plugs from provided assemblies...");
        List<TypeDefinition> plugs = _scanner.LoadPlugs(targetType, plugAssemblies);
        Console.WriteLine($"[PatchType] Found {plugs.Count} plug types to process");
        if (plugs.Count == 0)
            return;

        foreach (TypeDefinition plugType in plugs)
        {
            Console.WriteLine($"[PatchType] Processing members for plug type: {plugType.FullName}");
            ProcessPlugMembers(targetType, plugType);
        }

        Console.WriteLine($"[PatchType] Completed processing type: {targetType.FullName}");
    }

    private void ProcessPlugMembers(TypeDefinition targetType, TypeDefinition plugType)
    {
        Console.WriteLine($"[ProcessPlugMembers] Processing methods for {plugType.FullName}");
        foreach (MethodDefinition method in plugType.Methods.Where(m => !m.IsConstructor))
        {
            Console.WriteLine($"[ProcessPlugMembers] Processing method: {method.FullName}");
            CustomAttribute plugMemberAttr = method.CustomAttributes.FirstOrDefault(attr =>
                attr.AttributeType.FullName == typeof(PlugMemberAttribute).FullName);

            if (plugMemberAttr == null)
            {
                Console.WriteLine($"[ProcessPlugMembers] Skipping method without PlugMemberAttribute: {method.Name}");
                continue;
            }

            try
            {
                plugMemberAttr.ImportReferences(targetType.Module);
                ResolveAndPatchMethod(targetType, method, plugMemberAttr);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ProcessPlugMembers] ERROR processing method {method.FullName}: {ex}");
            }
        }

        Console.WriteLine($"[ProcessPlugMembers] Processing properties for {plugType.FullName}");
        foreach (PropertyDefinition property in plugType.Properties)
        {
            Console.WriteLine($"[ProcessPlugMembers] Processing property: {property.FullName}");
            CustomAttribute? plugMemberAttr = property.CustomAttributes.FirstOrDefault(attr =>
                attr.AttributeType.FullName == typeof(PlugMemberAttribute).FullName);

            if (plugMemberAttr == null)
            {
                Console.WriteLine(
                    $"[ProcessPlugMembers] Skipping property without PlugMemberAttribute: {property.Name}");
                continue;
            }

            string? targetPropertyName = plugMemberAttr.GetArgument(named: "TargetName", defaultValue: property.Name);
            Console.WriteLine($"[ProcessPlugMembers] Looking for target property: {targetPropertyName}");
            PropertyDefinition? targetProperty =
                targetType.Properties.FirstOrDefault(p => p.Name == targetPropertyName);

            if (targetProperty == null)
            {
                Console.WriteLine($"[ProcessPlugMembers] Target property not found: {targetPropertyName}");
                continue;
            }

            try
            {
                PatchProperty(targetProperty, property);
                Console.WriteLine($"[ProcessPlugMembers] Successfully patched property: {targetProperty.Name}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ProcessPlugMembers] ERROR patching property {targetPropertyName}: {ex}");
            }
        }

        Console.WriteLine($"[ProcessPlugMembers] Processing fields for {plugType.FullName}");
        foreach (FieldDefinition field in plugType.Fields)
        {
            Console.WriteLine($"[ProcessPlugMembers] Processing field: {field.FullName}");
            CustomAttribute? plugMemberAttr = field.CustomAttributes.FirstOrDefault(attr =>
                attr.AttributeType.FullName == typeof(PlugMemberAttribute).FullName);

            if (plugMemberAttr == null)
            {
                Console.WriteLine($"[ProcessPlugMembers] Skipping field without PlugMemberAttribute: {field.Name}");
                continue;
            }

            string? targetFieldName = plugMemberAttr.GetArgument(named: "TargetName", defaultValue: field.Name);
            Console.WriteLine($"[ProcessPlugMembers] Looking for target field: {targetFieldName}");
            FieldDefinition? targetField = targetType.Fields.FirstOrDefault(f =>
            {
                Console.WriteLine($"[ProcessPlugMembers] Target has field: {f.FullName}");
                return f.Name == targetFieldName;
            });

            if (targetField == null)
            {
                Console.WriteLine($"[ProcessPlugMembers] Target field not found: {targetFieldName}");
                continue;
            }

            try
            {
                PatchField(targetField, field);
                Console.WriteLine($"[ProcessPlugMembers] Successfully patched field: {targetField.Name}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ProcessPlugMembers] ERROR patching field {targetFieldName}: {ex}");
            }
        }
    }

    private void ResolveAndPatchMethod(TypeDefinition targetType, MethodDefinition plugMethod, CustomAttribute attr)
    {
        Console.WriteLine($"[ResolveAndPatchMethod] Starting method resolution for {plugMethod.FullName}");

        if (plugMethod.Name is "Ctor" or "CCtor")
        {
            bool isInstanceCtor = plugMethod.Parameters.Any(p => p.Name == "aThis");
            Console.WriteLine($"[ResolveAndPatchMethod] Handling constructor plug (Instance: {isInstanceCtor})");

            MethodDefinition? ctor = targetType.Methods.FirstOrDefault(m =>
                m.IsConstructor &&
                (isInstanceCtor
                    ? m.Parameters.Count + 1 == plugMethod.Parameters.Count
                    : m.Parameters.Count == plugMethod.Parameters.Count) &&
                (plugMethod.Name != "CCtor" || m.IsStatic));

            if (ctor != null)
            {
                Console.WriteLine($"[ResolveAndPatchMethod] Found target constructor: {ctor.FullName}");
                PatchMethod(ctor, plugMethod);
            }
            else
            {
                Console.WriteLine($"[ResolveAndPatchMethod] No matching constructor found for {plugMethod.FullName}");
                Console.WriteLine(
                    $"[ResolveAndPatchMethod] Parameters: {string.Join(", ", plugMethod.Parameters.Select(p => p.ParameterType + " " + p.Name))}");
            }

            return;
        }

        bool isInstancePlug = plugMethod.Parameters.Any(p => p.Name == "aThis");
        string? methodName = attr.GetArgument(named: "TargetName", defaultValue: plugMethod.Name);
        Console.WriteLine($"[ResolveAndPatchMethod] Resolving method: {methodName} (Instance: {isInstancePlug})");

        MethodDefinition? targetMethod = targetType.Methods.FirstOrDefault(m =>
            m.Name == methodName &&
            (isInstancePlug
                ? m.Parameters.Count + 1 == plugMethod.Parameters.Count
                : m.Parameters.Count == plugMethod.Parameters.Count));

        if (targetMethod != null)
        {
            Console.WriteLine($"[ResolveAndPatchMethod] Found target method: {targetMethod.FullName}");
            Console.WriteLine($"[ResolveAndPatchMethod] Parameters: {targetMethod.Parameters.Count}");
            PatchMethod(targetMethod, plugMethod);
        }
        else
        {
            Console.WriteLine($"[ResolveAndPatchMethod] Target method not found: {methodName}");
            Console.WriteLine(
                $"[ResolveAndPatchMethod] Expected parameters: {plugMethod.Parameters.Count - (isInstancePlug ? 1 : 0)}");
        }
    }

    public void PatchMethod(MethodDefinition targetMethod, MethodDefinition plugMethod, bool instance = false)
    {
        Console.WriteLine($"[PatchMethod] Starting method patch: {targetMethod.FullName} <- {plugMethod.FullName}");
        Console.WriteLine(
            $"[PatchMethod] Target method attributes: Static={targetMethod.IsStatic}, Constructor={targetMethod.IsConstructor}");
        Console.WriteLine($"[PatchMethod] Plug method parameters: {plugMethod.Parameters.Count}");

        targetMethod.Body ??= new MethodBody(targetMethod);
        ILProcessor processor = targetMethod.Body.GetILProcessor();
        Console.WriteLine($"[PatchMethod] Original instruction count: {targetMethod.Body.Instructions.Count}");

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
                Console.WriteLine(
                    $"[PatchMethod] Base Constructor Call found at index {index}, preserving {instructionsToKeep} instructions");
                while (targetMethod.Body.Instructions.Count > instructionsToKeep)
                {
                    processor.RemoveAt(instructionsToKeep);
                }
            }
            else
            {
                Console.WriteLine(
                    "[PatchMethod] No Base Constructor Call found in constructor, clearing all instructions");
                targetMethod.Body.Instructions.Clear();
            }
        }
        else
        {
            Console.WriteLine("[PatchMethod] Clearing non-constructor method body");
            targetMethod.Body.Instructions.Clear();
        }

        bool isInstance = plugMethod.Parameters.Any(p => p.Name == "aThis") || instance;
        Console.WriteLine($"[PatchMethod] Instance method: {isInstance}, Target static: {targetMethod.IsStatic}");

        if ((isInstance && !targetMethod.IsStatic) || targetMethod.IsConstructor)
        {
            Console.WriteLine($"[PatchMethod] Cloning {plugMethod.Body.Instructions.Count} instructions");
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
                    Console.WriteLine($"[PatchMethod] Cloned instruction {instruction} -> {clone}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PatchMethod] ERROR cloning instruction {instruction}: {ex}");
                    throw;
                }
            }
        }
        else
        {
            Console.WriteLine("[PatchMethod] Performing full method swap");
            try
            {
                targetMethod.SwapMethods(plugMethod);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PatchMethod] ERROR during method swap: {ex}");
                throw;
            }
        }

        if (targetMethod.Body.Instructions.Count == 0 || targetMethod.Body.Instructions[^1].OpCode != OpCodes.Ret)
        {
            Console.WriteLine("[PatchMethod] Adding final RET instruction");
            processor.Append(Instruction.Create(OpCodes.Ret));
        }

        Console.WriteLine($"[PatchMethod] Final instruction count: {targetMethod.Body.Instructions.Count}");
        Console.WriteLine($"[PatchMethod] Successfully patched method: {targetMethod.FullName}");
    }

    public void PatchProperty(PropertyDefinition targetProperty, PropertyDefinition plugProperty)
    {
        Console.WriteLine($"[PatchProperty] Patching property: {targetProperty.FullName}");

        if (plugProperty.GetMethod == null || plugProperty.SetMethod == null)
        {
            Console.WriteLine(
                $"[PatchProperty] No {(plugProperty.GetMethod == null ? "get" : "set")} method in plug property");
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
            Console.WriteLine("[PatchProperty] Patching set method");
            PatchMethod(targetProperty.SetMethod, plugProperty.SetMethod, true);
        }

        if (plugProperty.GetMethod != null)
        {
            Console.WriteLine("[PatchProperty] Patching get method");
            PatchMethod(targetProperty.GetMethod, plugProperty.GetMethod, true);
        }

        Console.WriteLine("[PatchProperty] Updating property attributes and type");
        targetProperty.Attributes = plugProperty.Attributes;
        targetProperty.PropertyType = targetProperty.Module.ImportReference(plugProperty.PropertyType);

        Console.WriteLine("[PatchProperty] Updating parameters");
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

        Console.WriteLine($"[PatchProperty] Completed property patch: {targetProperty.FullName}");
    }

    public void PatchField(FieldDefinition targetField, FieldDefinition plugField)
    {
        Console.WriteLine($"[PatchField] Patching field: {targetField.FullName}");
        ModuleDefinition module = targetField.Module;

        targetField.FieldType = module.ImportReference(plugField.FieldType);
        Console.WriteLine($"[PatchField] Type patched: {plugField.FieldType.FullName}");

        targetField.Attributes = plugField.Attributes;
        Console.WriteLine($"[PatchField] Attributes patched: {plugField.Attributes}");


        if (plugField.HasConstant)
        {
            targetField.Constant = plugField.Constant;
            targetField.HasConstant = true;
            Console.WriteLine($"[PatchField] Constant value set: {plugField.Constant}");
        }

        if (plugField.InitialValue != null)
        {
            targetField.InitialValue = [.. plugField.InitialValue];
            Console.WriteLine("[PatchField] InitialValue set");
        }

        if (plugField.MarshalInfo != null)
        {
            targetField.MarshalInfo = plugField.MarshalInfo;
            Console.WriteLine("[PatchField] MarshalInfo copied");
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
                processor.RemoveAt(targetFieldInstr.Index -
                                   1); // The previous index points to the IL of the field value
                processor.InsertBefore(targetFieldInstr.Instruction,
                    clone); // Replace the value of the field with the plugged value
            }
        }

        Console.WriteLine($"[PatchField] Completed field patch: {targetField.FullName}");
    }

    private void ReplaceFieldAccess(MethodDefinition method, FieldReference oldField, FieldReference newField,
        bool loadField = false)
    {
        Console.WriteLine(
            $"[ReplaceFieldAccess] Processing method: {method.FullName}, Field: {oldField.FullName}, LoadField: {loadField}");

        ILProcessor processor = method.Body.GetILProcessor();
        Collection<Instruction> instructions = [.. method.Body.Instructions];
        foreach (Instruction instruction in instructions)
        {
            if (instruction.Operand is not FieldReference fieldRef || fieldRef.FullName != oldField.FullName)
                continue;

            Console.WriteLine(
                $"[ReplaceFieldAccess] Replacing instruction: {instruction} in method: {method.FullName}");

            OpCode opcode = method.IsStatic
                ? loadField ? OpCodes.Ldsfld : OpCodes.Stsfld
                : loadField ? OpCodes.Ldfld : OpCodes.Stfld;
            Instruction newInstruction = Instruction.Create(opcode, method.Module.ImportReference(newField));
            processor.Replace(instruction, newInstruction);

            Console.WriteLine($"[ReplaceFieldAccess] Replaced: {instruction} -> {newInstruction}");
            Console.WriteLine(
                $"[ReplaceFieldAccess] Instruction in body: {method.Body.Instructions.IndexOf(newInstruction)}");
        }

        Console.WriteLine($"[ReplaceFieldAccess] Completed processing for method: {method.FullName}");
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
}
