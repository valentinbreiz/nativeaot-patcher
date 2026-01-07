using Cosmos.Patcher.Logging;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace Cosmos.Patcher.Patching;

/// <summary>
/// Handles patching of properties with plug implementations.
/// </summary>
public class PropertyPatcher
{
    private readonly IBuildLogger _log;
    private readonly MethodPatcher _methodPatcher;

    public PropertyPatcher(IBuildLogger log, MethodPatcher methodPatcher)
    {
        _log = log;
        _methodPatcher = methodPatcher;
    }

    /// <summary>
    /// Patches a target property with the implementation from a plug property.
    /// </summary>
    public void PatchProperty(TypeDefinition targetType, PropertyDefinition plugProperty, string? targetPropertyName = null)
    {
        PropertyDefinition? targetProperty = targetType.Properties.FirstOrDefault(p => p.Name == targetPropertyName);
        if (targetProperty == null)
        {
            _log.Warn($"Target property not found: {targetPropertyName}");
            return;
        }

        _log.Info($"Patching property: {targetProperty.FullName}");

        if (plugProperty.GetMethod == null || plugProperty.SetMethod == null)
        {
            _log.Warn($"No {(plugProperty.GetMethod == null ? "get" : "set")} method in plug property");
            return;
        }

        // Find backing fields
        var targetBackingField = FindBackingField(targetProperty.GetMethod);
        var plugBackingField = FindBackingField(plugProperty.GetMethod!);

        if (targetBackingField.Instruction == null || plugBackingField.Instruction == null)
        {
            _log.Warn("Could not find backing field for property");
            return;
        }

        // Patch get and set methods
        if (plugProperty.SetMethod != null && targetProperty.SetMethod != null)
        {
            _log.Debug("Patching set method");
            _methodPatcher.PatchMethod(targetProperty.SetMethod, plugProperty.SetMethod, true);
        }

        if (plugProperty.GetMethod != null && targetProperty.GetMethod != null)
        {
            _log.Debug("Patching get method");
            _methodPatcher.PatchMethod(targetProperty.GetMethod, plugProperty.GetMethod, true);
        }

        // Update property metadata
        UpdatePropertyMetadata(targetProperty, plugProperty);

        // Update backing field
        FieldReference targetFieldRef = (FieldReference)targetBackingField.Instruction.Operand;
        FieldReference plugFieldRef = (FieldReference)plugBackingField.Instruction.Operand;

        // Replace field access in accessors
        if (targetProperty.SetMethod != null)
        {
            ReplaceFieldAccess(targetProperty.SetMethod, plugFieldRef, targetFieldRef, false);
        }

        if (targetProperty.GetMethod != null)
        {
            ReplaceFieldAccess(targetProperty.GetMethod, plugFieldRef, targetFieldRef, true);
        }

        _log.Info($"Completed property patch: {targetProperty.FullName}");
    }

    /// <summary>
    /// Finds the backing field instruction in a property accessor.
    /// </summary>
    public (Instruction? Instruction, int Index) FindBackingField(MethodDefinition method)
    {
        return FindFieldInstruction(method, string.Empty, OpCodes.Stfld, OpCodes.Stsfld, OpCodes.Ldfld, OpCodes.Ldsfld);
    }

    /// <summary>
    /// Finds a field instruction in a method.
    /// </summary>
    public (Instruction? Instruction, int Index) FindFieldInstruction(MethodDefinition method, string? fieldName, params OpCode[] opcodes)
    {
        foreach (Instruction instruction in method.Body.Instructions)
        {
            if (!opcodes.Contains(instruction.OpCode) || instruction.Operand is not FieldReference fr)
                continue;

            if (!string.IsNullOrEmpty(fieldName) && fr.Resolve()?.Name != fieldName)
                continue;

            int idx = method.Body.Instructions.IndexOf(instruction);
            return (instruction, idx);
        }

        return default;
    }

    /// <summary>
    /// Updates property metadata from plug to target.
    /// </summary>
    private void UpdatePropertyMetadata(PropertyDefinition targetProperty, PropertyDefinition plugProperty)
    {
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
    }

    /// <summary>
    /// Replaces field access instructions in a method.
    /// </summary>
    private void ReplaceFieldAccess(MethodDefinition method, FieldReference oldField, FieldReference newField, bool loadField)
    {
        _log.Debug($"Replacing field access in {method.FullName}: {oldField.FullName} -> {newField.FullName}");

        ILProcessor processor = method.Body.GetILProcessor();
        Collection<Instruction> instructions = [.. method.Body.Instructions];

        foreach (Instruction instruction in instructions)
        {
            if (instruction.Operand is not FieldReference fieldRef || fieldRef.FullName != oldField.FullName)
                continue;

            _log.Debug($"Replacing instruction: {instruction}");

            OpCode opcode = method.IsStatic
                ? loadField ? OpCodes.Ldsfld : OpCodes.Stsfld
                : loadField ? OpCodes.Ldfld : OpCodes.Stfld;

            Instruction newInstruction = Instruction.Create(opcode, method.Module.ImportReference(newField));
            processor.Replace(instruction, newInstruction);

            _log.Debug($"Replaced: {instruction} -> {newInstruction}");
        }
    }
}
