using System.Diagnostics.CodeAnalysis;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;

namespace Liquip.Patcher.Extensions;

public static class MethodBodyEx
{

    [return: NotNullIfNotNull("bo")]
    public static MethodDefinition? ReplaceMethodWithJump(this MethodDefinition? bo, MethodDefinition m)
    {
        Helpers.ThrowIfArgumentNull(m);

        if (bo == null)
            return null;
                
        var bc = new MethodBody(m);

        if (!bo.HasParameters)
        {
            var jump = Instruction.Create(OpCodes.Call, m);
            bc.Instructions.Add(jump);
            bc.Instructions.Add(Instruction.Create(OpCodes.Ret));
            
        }
        

        
        bo.Body = bc;
        
        return bo;
    }
    
    /// <summary>
    /// replace method body
    /// </summary>
    /// <param name="newBody"></param>
    /// <param name="m"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    [return: NotNullIfNotNull("newBody")]
    public static MethodBody? ReplaceMethodBody(this MethodBody? newBody, MethodDefinition m)
    {
        Helpers.ThrowIfArgumentNull(m);

        if (newBody == null)
            return null;

        var bc = new MethodBody(m);
        bc.MaxStackSize = newBody.MaxStackSize;
        bc.InitLocals = newBody.InitLocals;
        bc.LocalVarToken = newBody.LocalVarToken;

        bc.Instructions.AddRange(newBody.Instructions.Select(o =>
        {
            var c = Instruction.Create(OpCodes.Nop);
            c.OpCode = o.OpCode;
            c.Operand = o.Operand;
            c.Offset = o.Offset;
            return c;
        }));

        foreach (var instruction in bc.Instructions)
        {
            if (instruction.Operand is Instruction target)
            {
                instruction.Operand = bc.Instructions[newBody.Instructions.IndexOf(target)];
            }
            else if (instruction.Operand is Instruction[] targets)
            {
                instruction.Operand = targets
                    .Select(i => bc.Instructions[newBody.Instructions.IndexOf(i)])
                    .ToArray();
            }
        }

        bc.ExceptionHandlers.AddRange(newBody.ExceptionHandlers.Select(o =>
        {
            var c = new ExceptionHandler(o.HandlerType);
            c.TryStart = o.TryStart == null ? null : bc.Instructions[newBody.Instructions.IndexOf(o.TryStart)];
            c.TryEnd = o.TryEnd == null ? null : bc.Instructions[newBody.Instructions.IndexOf(o.TryEnd)];
            c.FilterStart = o.FilterStart == null ? null : bc.Instructions[newBody.Instructions.IndexOf(o.FilterStart)];
            c.HandlerStart = o.HandlerStart == null ? null : bc.Instructions[newBody.Instructions.IndexOf(o.HandlerStart)];
            c.HandlerEnd = o.HandlerEnd == null ? null : bc.Instructions[newBody.Instructions.IndexOf(o.HandlerEnd)];
            c.CatchType = o.CatchType;
            return c;
        }));

        bc.Variables.AddRange(newBody.Variables.Select(o =>
        {
            var c = new VariableDefinition(o.VariableType);
            return c;
        }));

        Instruction ResolveInstrOff(int off)
        {
            // Can't check cloned instruction offsets directly, as those can change for some reason
            for (var i = 0; i < newBody.Instructions.Count; i++)
                if (newBody.Instructions[i].Offset == off)
                    return bc.Instructions[i];
            throw new ArgumentException($"Invalid instruction offset {off}");
        }

        m.CustomDebugInformations.AddRange(newBody.Method.CustomDebugInformations.Select(o =>
        {
            if (o is AsyncMethodBodyDebugInformation ao)
            {
                var c = new AsyncMethodBodyDebugInformation();
                if (ao.CatchHandler.Offset >= 0)
                    c.CatchHandler = ao.CatchHandler.IsEndOfMethod ? new InstructionOffset() : new InstructionOffset(ResolveInstrOff(ao.CatchHandler.Offset));
                c.Yields.AddRange(ao.Yields.Select(off => off.IsEndOfMethod ? new InstructionOffset() : new InstructionOffset(ResolveInstrOff(off.Offset))));
                c.Resumes.AddRange(ao.Resumes.Select(off => off.IsEndOfMethod ? new InstructionOffset() : new InstructionOffset(ResolveInstrOff(off.Offset))));
                c.ResumeMethods.AddRange(ao.ResumeMethods);
                return c;
            }
            else if (o is StateMachineScopeDebugInformation so)
            {
                var c = new StateMachineScopeDebugInformation();
                c.Scopes.AddRange(so.Scopes.Select(s => new StateMachineScope(ResolveInstrOff(s.Start.Offset), s.End.IsEndOfMethod ? null : ResolveInstrOff(s.End.Offset))));
                return c;
            }
            else
                return o;
        }));

        m.DebugInformation.SequencePoints.AddRange(newBody.Method.DebugInformation.SequencePoints.Select(o =>
        {
            var c = new SequencePoint(ResolveInstrOff(o.Offset), o.Document);
            c.StartLine = o.StartLine;
            c.StartColumn = o.StartColumn;
            c.EndLine = o.EndLine;
            c.EndColumn = o.EndColumn;
            return c;
        }));

        return bc;
    }
    
}