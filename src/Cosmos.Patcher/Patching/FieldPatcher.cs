using System.Linq;
using Cosmos.Patcher.IL;
using Cosmos.Patcher.Logging;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using MonoMod.Utils;

namespace Cosmos.Patcher.Patching;

/// <summary>
/// Handles patching of fields with plug implementations.
/// </summary>
public class FieldPatcher
{
    private readonly IBuildLogger _log;

    public FieldPatcher(IBuildLogger log)
    {
        _log = log;
    }

    /// <summary>
    /// Patches a target field with the definition from a plug field.
    /// </summary>
    public void PatchField(TypeDefinition targetType, FieldDefinition plugField, string? targetFieldName = null)
    {
        FieldDefinition? targetField = targetType.Fields.FirstOrDefault(f => f.Name == targetFieldName);
        if (targetField == null)
        {
            _log.Warn($"Target field not found: {targetFieldName}");
            return;
        }

        _log.Info($"Patching field: {targetField.FullName}");
        ModuleDefinition module = targetField.Module;

        // Patch field type
        targetField.FieldType = TypeImporter.SafeImportType(module, plugField.FieldType);
        _log.Debug($"Type patched: {plugField.FieldType.FullName}");

        // Patch attributes
        targetField.Attributes = plugField.Attributes;
        _log.Debug($"Attributes patched: {plugField.Attributes}");

        // Patch constant value
        if (plugField.HasConstant)
        {
            targetField.Constant = plugField.Constant;
            targetField.HasConstant = true;
            _log.Debug($"Constant value set: {plugField.Constant}");
        }

        // Patch initial value
        if (plugField.InitialValue != null)
        {
            targetField.InitialValue = [.. plugField.InitialValue];
            _log.Debug("InitialValue set");
        }

        // Patch marshal info
        if (plugField.MarshalInfo != null)
        {
            targetField.MarshalInfo = plugField.MarshalInfo;
            _log.Debug("MarshalInfo copied");
        }

        // Patch field initializers in constructors
        PatchFieldInitializers(targetField, plugField, module);

        _log.Info($"Completed field patch: {targetField.FullName}");
    }

    /// <summary>
    /// Patches field initialization code in constructors.
    /// </summary>
    private void PatchFieldInitializers(FieldDefinition targetField, FieldDefinition plugField, ModuleDefinition module)
    {
        foreach (MethodDefinition targetCtor in targetField.DeclaringType.GetConstructors())
        {
            foreach (MethodDefinition plugCtor in plugField.DeclaringType.GetConstructors())
            {
                var targetFieldInstr = FindFieldInstruction(targetCtor, targetField, OpCodes.Stfld, OpCodes.Stsfld);
                var plugFieldInstr = FindFieldInstruction(plugCtor, plugField, OpCodes.Stfld, OpCodes.Stsfld);

                if (targetFieldInstr.Instruction == null || plugFieldInstr.Instruction == null)
                    continue;

                // Get the instruction that loads the field value
                Instruction plugFieldValue = plugCtor.Body.Instructions[plugFieldInstr.Index - 1];
                Instruction clone = plugFieldValue.Clone();

                // Import references in the operand
                clone.Operand = plugFieldValue.Operand switch
                {
                    MethodReference m => TypeImporter.SafeImportMethod(module, m),
                    FieldReference f => TypeImporter.SafeImportField(module, f),
                    TypeReference t => TypeImporter.SafeImportType(module, t),
                    MemberReference mr => module.ImportReference(mr),
                    _ => plugFieldValue.Operand
                };

                // Replace the value instruction
                ILProcessor processor = targetCtor.Body.GetILProcessor();
                processor.RemoveAt(targetFieldInstr.Index - 1);
                processor.InsertBefore(targetFieldInstr.Instruction, clone);

                _log.Debug($"Patched field initializer in {targetCtor.Name}");
            }
        }
    }

    /// <summary>
    /// Finds a field store/load instruction in a method.
    /// </summary>
    private static (Instruction? Instruction, int Index) FindFieldInstruction(MethodDefinition method, FieldDefinition field, params OpCode[] opcodes)
    {
        foreach (Instruction instruction in method.Body.Instructions)
        {
            if (!opcodes.Contains(instruction.OpCode) || instruction.Operand is not FieldReference fr)
                continue;

            if (fr.Resolve()?.Name != field.Name)
                continue;

            int idx = method.Body.Instructions.IndexOf(instruction);
            return (instruction, idx);
        }

        return default;
    }
}
