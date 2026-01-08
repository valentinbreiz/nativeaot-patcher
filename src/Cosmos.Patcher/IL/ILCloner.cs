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
    /// Handles generic parameter remapping for generic methods.
    /// </summary>
    public object? RemapOperand(object? operand, MethodDefinition sourceMethod, MethodDefinition targetMethod, bool isInstancePlug)
    {
        // Check if we need to do generic remapping
        bool needsGenericRemap = sourceMethod.HasGenericParameters && targetMethod.HasGenericParameters;

        return operand switch
        {
            MethodReference m => RemapMethodReference(m, sourceMethod, targetMethod, needsGenericRemap),
            FieldReference f => TypeImporter.SafeImportField(targetMethod.Module, f),
            TypeReference t => needsGenericRemap
                ? TypeImporter.SafeImportTypeWithGenericRemap(targetMethod.Module, t, sourceMethod, targetMethod)
                : TypeImporter.SafeImportType(targetMethod.Module, t),
            MemberReference mr => targetMethod.Module.ImportReference(mr),
            ParameterDefinition p => RemapParameter(p, sourceMethod, targetMethod, isInstancePlug),
            _ => operand
        };
    }

    /// <summary>
    /// Remaps a method reference, handling generic parameter remapping for generic method calls.
    /// </summary>
    private MethodReference RemapMethodReference(MethodReference methodRef, MethodDefinition sourceMethod, MethodDefinition targetMethod, bool needsGenericRemap)
    {
        if (!needsGenericRemap)
            return TypeImporter.SafeImportMethod(targetMethod.Module, methodRef);

        // If this is a generic instance method, we need to remap the generic arguments
        if (methodRef is GenericInstanceMethod gim)
        {
            var imported = TypeImporter.SafeImportMethod(targetMethod.Module, gim.ElementMethod);

            // Create new generic instance with remapped arguments
            var newGim = new GenericInstanceMethod(imported);
            foreach (var arg in gim.GenericArguments)
            {
                var remappedArg = TypeImporter.SafeImportTypeWithGenericRemap(targetMethod.Module, arg, sourceMethod, targetMethod);
                newGim.GenericArguments.Add(remappedArg);
            }
            return newGim;
        }

        // For non-generic method references, still need to remap parameter/return types that use generics
        var result = TypeImporter.SafeImportMethod(targetMethod.Module, methodRef);

        // Check if declaring type uses our generic parameters
        if (result.DeclaringType != null)
        {
            var remappedDeclaringType = TypeImporter.SafeImportTypeWithGenericRemap(
                targetMethod.Module, result.DeclaringType, sourceMethod, targetMethod);

            if (remappedDeclaringType != result.DeclaringType)
            {
                // Need to create a new method reference with the remapped declaring type
                var newRef = new MethodReference(result.Name, result.ReturnType, remappedDeclaringType)
                {
                    HasThis = result.HasThis,
                    ExplicitThis = result.ExplicitThis,
                    CallingConvention = result.CallingConvention
                };

                foreach (var param in result.Parameters)
                {
                    var remappedParamType = TypeImporter.SafeImportTypeWithGenericRemap(
                        targetMethod.Module, param.ParameterType, sourceMethod, targetMethod);
                    newRef.Parameters.Add(new ParameterDefinition(param.Name, param.Attributes, remappedParamType));
                }

                // Remap return type
                newRef.ReturnType = TypeImporter.SafeImportTypeWithGenericRemap(
                    targetMethod.Module, result.ReturnType, sourceMethod, targetMethod);

                return newRef;
            }
        }

        return result;
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
    /// Handles generic parameter remapping for generic methods.
    /// </summary>
    public static void CopyVariables(MethodDefinition sourceMethod, MethodDefinition targetMethod)
    {
        targetMethod.Body.Variables.Clear();

        bool needsGenericRemap = sourceMethod.HasGenericParameters && targetMethod.HasGenericParameters;

        foreach (VariableDefinition variable in sourceMethod.Body.Variables)
        {
            TypeReference varType = needsGenericRemap
                ? TypeImporter.SafeImportTypeWithGenericRemap(targetMethod.Module, variable.VariableType, sourceMethod, targetMethod)
                : TypeImporter.SafeImportType(targetMethod.Module, variable.VariableType);

            targetMethod.Body.Variables.Add(new VariableDefinition(varType));
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
