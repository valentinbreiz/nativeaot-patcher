using Cosmos.Patcher.Logging;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;

namespace Cosmos.Patcher.IL;

/// <summary>
/// Handles IL instruction cloning, operand remapping, and branch target fixing.
/// </summary>
public class ILCloner
{
    private readonly IBuildLogger _log;

    public ILCloner(IBuildLogger log)
    {
        _log = log;
    }

    /// <summary>
    /// Clones all instructions from the source method to the target method.
    /// Handles operand importing and parameter remapping.
    /// </summary>
    public Dictionary<Instruction, Instruction> CloneInstructions(
        MethodDefinition sourceMethod,
        MethodDefinition targetMethod,
        ILProcessor processor,
        bool isInstancePlug)
    {
        var instructionMap = new Dictionary<Instruction, Instruction>();

        foreach (Instruction instruction in sourceMethod.Body.Instructions)
        {
            Instruction clone = instruction.Clone();
            clone.Operand = RemapOperand(instruction.Operand, sourceMethod, targetMethod, isInstancePlug);
            processor.Append(clone);
            instructionMap[instruction] = clone;
            _log.Debug($"Cloned instruction {instruction} -> {clone}");
        }

        return instructionMap;
    }

    /// <summary>
    /// Remaps an instruction operand to the target method's context.
    /// </summary>
    public object? RemapOperand(object? operand, MethodDefinition sourceMethod, MethodDefinition targetMethod, bool isInstancePlug)
    {
        return operand switch
        {
            MethodReference m => TypeImporter.SafeImportMethod(targetMethod.Module, m),
            FieldReference f => TypeImporter.SafeImportField(targetMethod.Module, f),
            TypeReference t => TypeImporter.SafeImportType(targetMethod.Module, t),
            MemberReference mr => targetMethod.Module.ImportReference(mr),
            ParameterDefinition p => RemapParameter(p, sourceMethod, targetMethod, isInstancePlug),
            _ => operand
        };
    }

    /// <summary>
    /// Fix branch instruction targets to point to cloned instructions instead of original source instructions.
    /// </summary>
    public static void FixBranchTargets(Dictionary<Instruction, Instruction> instructionMap)
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
    public static object RemapParameter(ParameterDefinition plugParam, MethodDefinition plugMethod,
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

    /// <summary>
    /// Copies variables from source to target method with proper type importing.
    /// </summary>
    public static void CopyVariables(MethodDefinition sourceMethod, MethodDefinition targetMethod)
    {
        targetMethod.Body.Variables.Clear();
        foreach (VariableDefinition variable in sourceMethod.Body.Variables)
        {
            targetMethod.Body.Variables.Add(
                new VariableDefinition(TypeImporter.SafeImportType(targetMethod.Module, variable.VariableType))
            );
        }
    }

    /// <summary>
    /// Copies exception handlers from source to target method.
    /// </summary>
    public static void CopyExceptionHandlers(
        MethodDefinition sourceMethod,
        MethodDefinition targetMethod,
        Dictionary<Instruction, Instruction> instructionMap)
    {
        if (!sourceMethod.Body.HasExceptionHandlers)
            return;

        foreach (ExceptionHandler handler in sourceMethod.Body.ExceptionHandlers)
        {
            var newHandler = new ExceptionHandler(handler.HandlerType);

            int tryStartIndex = sourceMethod.Body.Instructions.IndexOf(handler.TryStart);
            int tryEndIndex = sourceMethod.Body.Instructions.IndexOf(handler.TryEnd);
            int handlerStartIndex = sourceMethod.Body.Instructions.IndexOf(handler.HandlerStart);

            newHandler.TryStart = targetMethod.Body.Instructions[tryStartIndex];
            newHandler.TryEnd = targetMethod.Body.Instructions[tryEndIndex];
            newHandler.HandlerStart = targetMethod.Body.Instructions[handlerStartIndex];

            if (handler.HandlerEnd != null)
            {
                int handlerEndIndex = sourceMethod.Body.Instructions.IndexOf(handler.HandlerEnd);
                newHandler.HandlerEnd = targetMethod.Body.Instructions[handlerEndIndex];
            }

            if (handler.CatchType != null)
            {
                newHandler.CatchType = TypeImporter.SafeImportType(targetMethod.Module, handler.CatchType);
            }

            if (handler.FilterStart != null)
            {
                int filterStartIndex = sourceMethod.Body.Instructions.IndexOf(handler.FilterStart);
                newHandler.FilterStart = targetMethod.Body.Instructions[filterStartIndex];
            }

            targetMethod.Body.ExceptionHandlers.Add(newHandler);
        }
    }

    /// <summary>
    /// Copies method body properties (InitLocals, MaxStackSize).
    /// </summary>
    public static void CopyBodyProperties(MethodDefinition sourceMethod, MethodDefinition targetMethod)
    {
        targetMethod.Body.InitLocals = sourceMethod.Body.InitLocals;
        targetMethod.Body.MaxStackSize = sourceMethod.Body.MaxStackSize;
    }
}
