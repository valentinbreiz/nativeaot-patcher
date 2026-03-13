// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Patcher.Debug;
using Cosmos.Patcher.IL;
using Cosmos.Patcher.Resolution;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Cosmos.Patcher;

public sealed partial class PlugPatcher
{
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
                _log.Debug($"Target prototype: {DebugHelpers.FormatMethod(ctor)}");
                _log.Debug($"Plug prototype: {DebugHelpers.FormatMethod(plugMethod)}");
                PatchMethod(ctor, plugMethod);
            }

            return;
        }

        MethodDefinition? targetMethod =
            _methodResolver.ResolveMethod(targetType, plugMethod, targetMethodName ?? plugMethod.Name);
        if (targetMethod != null)
        {
            _log.Debug($"Target prototype: {DebugHelpers.FormatMethod(targetMethod)}");
            PatchMethod(targetMethod, plugMethod);
        }
        else
        {
            _methodResolver.DumpOverloads(targetType, targetMethodName ?? plugMethod.Name, plugMethod);
        }
    }

    /// <summary>
    /// Patches a target method with the implementation from a plug method.
    /// </summary>
    public void PatchMethod(MethodDefinition targetMethod, MethodDefinition plugMethod, bool forceInstance = false)
    {
        _log.Info($"Patching method: {targetMethod.FullName} <- {plugMethod.FullName}");
        _log.Debug($"Target: Static={targetMethod.IsStatic}, Constructor={targetMethod.IsConstructor}");
        _log.Debug($"Plug parameters: {plugMethod.Parameters.Count}");

        targetMethod.Body ??= new MethodBody(targetMethod);
        ILProcessor processor = targetMethod.Body.GetILProcessor();
        _log.Debug($"Original instruction count: {targetMethod.Body.Instructions.Count}");

        bool isInstance = MethodResolver.IsInstancePlug(plugMethod) || forceInstance;
        _log.Debug($"Instance method: {isInstance}, Target static: {targetMethod.IsStatic}");

        // Prepare method body
        PrepareMethodBody(targetMethod, processor);

        // Clone instructions
        CloneMethodBody(targetMethod, plugMethod, processor, isInstance);

        // Remove P/Invoke metadata
        RemovePInvokeMetadata(targetMethod);

        // Ensure method ends with ret
        EnsureRetInstruction(targetMethod, processor);

        _log.Debug($"Final instruction count: {targetMethod.Body.Instructions.Count}");
        _log.Info($"Successfully patched method: {targetMethod.FullName}");
    }

    /// <summary>
    /// Prepares the target method body for patching.
    /// For constructors, preserves the base constructor call.
    /// </summary>
    private void PrepareMethodBody(MethodDefinition targetMethod, ILProcessor processor)
    {
        if (targetMethod.IsConstructor)
        {
            Instruction? baseCtorCall = FindBaseConstructorCall(targetMethod);

            if (baseCtorCall != null)
            {
                int index = targetMethod.Body.Instructions.IndexOf(baseCtorCall);
                int instructionsToKeep = index + 1;
                _log.Debug(
                    $"Base constructor call found at index {index}, preserving {instructionsToKeep} instructions");

                while (targetMethod.Body.Instructions.Count > instructionsToKeep)
                {
                    processor.RemoveAt(instructionsToKeep);
                }
            }
            else
            {
                _log.Debug("No base constructor call found, clearing all instructions");
                targetMethod.Body.Instructions.Clear();
            }
        }
        else
        {
            _log.Debug("Clearing non-constructor method body");
            targetMethod.Body.Instructions.Clear();
        }
    }

    /// <summary>
    /// Finds the base constructor call in a constructor method.
    /// </summary>
    private static Instruction? FindBaseConstructorCall(MethodDefinition method)
    {
        return method.Body.Instructions.FirstOrDefault(i =>
            i.OpCode == OpCodes.Call &&
            i.Operand is MethodReference { Name: ".ctor" } ctorRef &&
            (ctorRef.DeclaringType == method.DeclaringType ||
             ctorRef.DeclaringType == method.DeclaringType.BaseType));
    }

    /// <summary>
    /// Clones the method body from plug to target.
    /// </summary>
    private void CloneMethodBody(MethodDefinition targetMethod, MethodDefinition plugMethod, ILProcessor processor,
        bool isInstance)
    {
        // Clear variables and exception handlers
        targetMethod.Body.Variables.Clear();
        targetMethod.Body.ExceptionHandlers.Clear();

        // Copy variables
        ILCloner.CopyVariables(plugMethod, targetMethod);
        _log.Debug($"Cloning {plugMethod.Body.Instructions.Count} instructions");

        // Clone instructions
        var instructionMap = ILCloner.CloneInstructions(plugMethod, targetMethod, processor, isInstance);
        foreach (var instruction in instructionMap)
            _log.Debug($"Cloned instruction: {instruction.Key} -> {instruction.Value}");

        // Fix branch targets
        ILCloner.FixBranchTargets(instructionMap);

        // Copy exception handlers (for non-constructor full swap)
        if (!targetMethod.IsConstructor || !isInstance)
            ILCloner.CopyExceptionHandlers(plugMethod, targetMethod, instructionMap);

        // Copy body properties
        ILCloner.CopyBodyProperties(plugMethod, targetMethod);

        DebugHelpers.DumpIL(_log, targetMethod);
        _log.Debug(
            $"Cloned {targetMethod.Body.Instructions.Count} instructions with {targetMethod.Body.Variables.Count} variables and {targetMethod.Body.ExceptionHandlers.Count} exception handlers");
    }

    /// <summary>
    /// Removes P/Invoke metadata so the runtime treats the method as managed.
    /// </summary>
    private void RemovePInvokeMetadata(MethodDefinition method)
    {
        if (!method.IsPInvokeImpl && method.PInvokeInfo == null)
            return;

        _log.Debug("Removing P/Invoke metadata");
        method.PInvokeInfo = null;
        method.Attributes &= ~MethodAttributes.PInvokeImpl;
        method.ImplAttributes &= ~MethodImplAttributes.PreserveSig;
        method.ImplAttributes &= ~MethodImplAttributes.InternalCall;
        method.ImplAttributes &= ~MethodImplAttributes.Native;
        method.ImplAttributes &= ~MethodImplAttributes.Unmanaged;
        method.ImplAttributes &= ~MethodImplAttributes.Runtime;
    }

    /// <summary>
    /// Ensures the method ends with a ret instruction.
    /// </summary>
    private void EnsureRetInstruction(MethodDefinition method, ILProcessor processor)
    {
        if (method.Body.Instructions.Count != 0 && method.Body.Instructions[^1].OpCode == OpCodes.Ret)
            return;

        _log.Debug("Adding final RET instruction");
        processor.Append(Instruction.Create(OpCodes.Ret));
    }
}
