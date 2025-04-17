using System;
using System.Collections.Generic;
using System.Linq;
using Cosmos.API.Attributes;
using Cosmos.API.Enum;
using Mono.Cecil;
using Mono.Cecil.Cil;
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
        if (plugAssemblies == null || plugAssemblies.Length == 0)
        {
            Console.WriteLine("[PatchType] ERROR: No plug assemblies provided");
            throw new ArgumentNullException(nameof(plugAssemblies));
        }

        if (targetType.CustomAttributes.Any(attr => attr.AttributeType.FullName == typeof(PlugAttribute).FullName))
        {
            Console.WriteLine($"[PatchType] Skipping type marked with PlugAttribute: {targetType.FullName}");
            return;
        }

        Console.WriteLine("[PatchType] Loading plugs from provided assemblies...");
        List<TypeDefinition> plugs = _scanner.LoadPlugs(plugAssemblies);
        Console.WriteLine($"[PatchType] Found {plugs.Count} plug types to process");

        foreach (TypeDefinition plugType in plugs)
        {
            Console.WriteLine($"[PatchType] Processing plug type: {plugType.FullName}");
            CustomAttribute plugAttr = plugType.CustomAttributes
                .FirstOrDefault(a => a.AttributeType.FullName == typeof(PlugAttribute).FullName);

            if (plugAttr == null)
            {
                Console.WriteLine($"[PatchType] Skipping plug type without PlugAttribute: {plugType.FullName}");
                continue;
            }

            try
            {
                plugAttr.ImportReferences(targetType.Module);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PatchType] ERROR importing references for {plugType.FullName}: {ex}");
                continue;
            }

            string targetTypeName = plugAttr.GetArgument<string>(fallbackArgs: new object[] { 0, "Target" })
                                    ?? plugAttr.GetArgument<string>(fallbackArgs: new object[] { 0, "TargetName" });

            if (string.IsNullOrEmpty(targetTypeName))
            {
                Console.WriteLine(
                    $"[PatchType] WARNING: Plug {plugType.FullName} has invalid/missing target type specification");
                continue;
            }

            if (targetTypeName != targetType.FullName)
            {
                Console.WriteLine(
                    $"[PatchType] Plug target {targetTypeName} does not match current type {targetType.FullName}, skipping");
                continue;
            }

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
            CustomAttribute plugMemberAttr = property.CustomAttributes.FirstOrDefault(attr =>
                attr.AttributeType.FullName == typeof(PlugMemberAttribute).FullName);

            if (plugMemberAttr == null)
            {
                Console.WriteLine(
                    $"[ProcessPlugMembers] Skipping property without PlugMemberAttribute: {property.Name}");
                continue;
            }

            string targetPropertyName = plugMemberAttr.GetArgument(property.FullName, 0, "TargetName");
            Console.WriteLine($"[ProcessPlugMembers] Looking for target property: {targetPropertyName}");
            PropertyDefinition? targetProperty =
                targetType.Properties.FirstOrDefault(p => p.FullName == targetPropertyName);

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

            string? targetFieldName = plugMemberAttr.GetArgument(field.FullName, 0, "TargetName");
            Console.WriteLine($"[ProcessPlugMembers] Looking for target field: {targetFieldName}");
            FieldDefinition? targetField = targetType.Fields.FirstOrDefault(f => f.FullName == targetFieldName);

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

            MethodDefinition ctor = targetType.Methods.FirstOrDefault(m =>
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
        string methodName = attr.GetArgument(plugMethod.Name, 0, "TargetName");
        Console.WriteLine($"[ResolveAndPatchMethod] Resolving method: {methodName} (Instance: {isInstancePlug})");

        MethodDefinition targetMethod = targetType.Methods.FirstOrDefault(m =>
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

    public void PatchMethod(MethodDefinition targetMethod, MethodDefinition plugMethod)
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
            Instruction nop = targetMethod.Body.Instructions.FirstOrDefault(i => i.OpCode == OpCodes.Nop);
            if (nop != null)
            {
                int index = targetMethod.Body.Instructions.IndexOf(nop);
                Console.WriteLine(
                    $"[PatchMethod] Constructor NOP found at index {index}, preserving {index + 1} instructions");
                while (targetMethod.Body.Instructions.Count > index + 1)
                {
                    processor.Remove(targetMethod.Body.Instructions[index + 1]);
                }
            }
            else
            {
                Console.WriteLine($"[PatchMethod] No NOP found in constructor, clearing all instructions");
                targetMethod.Body.Instructions.Clear();
            }
        }
        else
        {
            Console.WriteLine($"[PatchMethod] Clearing non-constructor method body");
            targetMethod.Body.Instructions.Clear();
        }

        bool isInstance = plugMethod.Parameters.Any(p => p.Name == "aThis");
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
            Console.WriteLine($"[PatchMethod] Performing full method swap");
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
            Console.WriteLine($"[PatchMethod] Adding final RET instruction");
            processor.Append(Instruction.Create(OpCodes.Ret));
        }

        Console.WriteLine($"[PatchMethod] Final instruction count: {targetMethod.Body.Instructions.Count}");
        Console.WriteLine($"[PatchMethod] Successfully patched method: {targetMethod.FullName}");
    }

    public void PatchProperty(PropertyDefinition targetProperty, PropertyDefinition plugProperty)
    {
        Console.WriteLine($"[PatchProperty] Patching property: {targetProperty.FullName}");

        if (plugProperty.SetMethod != null)
        {
            Console.WriteLine($"[PatchProperty] Patching set method");
            PatchMethod(targetProperty.SetMethod, plugProperty.SetMethod);
        }
        else
        {
            Console.WriteLine($"[PatchProperty] No set method in plug property");
        }

        if (plugProperty.GetMethod != null)
        {
            Console.WriteLine($"[PatchProperty] Patching get method");
            PatchMethod(targetProperty.GetMethod, plugProperty.GetMethod);
        }
        else
        {
            Console.WriteLine($"[PatchProperty] No get method in plug property");
        }

        Console.WriteLine($"[PatchProperty] Updating property attributes and type");
        targetProperty.Attributes = plugProperty.Attributes;
        targetProperty.PropertyType = targetProperty.Module.ImportReference(plugProperty.PropertyType);

        Console.WriteLine($"[PatchProperty] Updating parameters");
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
        Console.WriteLine($"[PatchProperty] Completed property patch: {targetProperty.FullName}");
    }

    public void PatchField(FieldDefinition targetField, FieldDefinition plugField)
    {
        Console.WriteLine($"[PatchField] Patching field: {targetField.FullName}");
        ModuleDefinition module = targetField.Module;

        Console.WriteLine(
            $"[PatchField] Old type: {targetField.FieldType.FullName}, New type: {plugField.FieldType.FullName}");
        targetField.FieldType = module.ImportReference(plugField.FieldType);

        Console.WriteLine($"[PatchField] Setting field attributes: {plugField.Attributes}");
        targetField.Attributes = plugField.Attributes;

        targetField.Constant = plugField.Constant;
        targetField.HasConstant = plugField.HasConstant;
        Console.WriteLine($"[PatchField] Completed field patch: {targetField.FullName}");
    }
}
