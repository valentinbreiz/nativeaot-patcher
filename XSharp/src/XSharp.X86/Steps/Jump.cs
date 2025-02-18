// This code is licensed under MIT license (see LICENSE for details)

using XSharp.Base.ControlFlow;
using XSharp.X86.Interfaces;

namespace XSharp.X86.Steps;

public static class JumpEx
{
    public static T Jump<T>(this T t, LabelObject label)
    where T : IX86
    {
        t.Raw($"jum {label}");
        return t;
    }

    public static T Jump<T>(this T t, LabelObject label, JumpCondition condition)
        where T : IX86
    {
        return condition switch
        {
            JumpCondition.Zero => t.Raw($"jz {label}"),
            JumpCondition.Equal => t.Raw($"je {label}"),
            JumpCondition.NotZero => t.Raw($"jnz {label}"),
            JumpCondition.NotEqual => t.Raw($"jne {label}"),
            JumpCondition.Carry => t.Raw($"jc {label}"),
            JumpCondition.NotCarry => t.Raw($"jnc {label}"),
            JumpCondition.Overflow => t.Raw($"jo {label}"),
            JumpCondition.NotOverflow => t.Raw($"jno {label}"),
            JumpCondition.Signed => t.Raw($"js {label}"),
            JumpCondition.NotSigned => t.Raw($"jns {label}"),
            JumpCondition.Parity => t.Raw($"jp {label}"),
            JumpCondition.ParityIsEven => t.Raw($"jpe {label}"),
            JumpCondition.NotParity => t.Raw($"jnp {label}"),
            JumpCondition.ParityIsOdd => t.Raw($"jpo {label}"),
            JumpCondition.CxIsZero => t.Raw($"jcxz {label}"),
            JumpCondition.EcxIsZero => t.Raw($"jecxz {label}"),
            JumpCondition.Greater => t.Raw($"jg {label}"),
            JumpCondition.NotGreater => t.Raw($"jng {label}"),
            JumpCondition.LessOrEqual => t.Raw($"jle {label}"),
            JumpCondition.NotLessOrEqual => t.Raw($"jnle {label}"),
            JumpCondition.GreaterOrEqual => t.Raw($"jge {label}"),
            JumpCondition.NotGreaterOrEqual => t.Raw($"jnge {label}"),
            JumpCondition.Less => t.Raw($"jl {label}"),
            JumpCondition.NotLess => t.Raw($"jnl {label}"),
            JumpCondition.Above => t.Raw($"ja {label}"),
            JumpCondition.NotAbove => t.Raw($"jna {label}"),
            JumpCondition.AboveOrEqual => t.Raw($"jae {label}"),
            JumpCondition.NotAboveOrEqual => t.Raw($"jnae {label}"),
            JumpCondition.Below => t.Raw($"jb {label}"),
            JumpCondition.NotBelow => t.Raw($"jnb {label}"),
            JumpCondition.BelowOrEqual => t.Raw($"jbe {label}"),
            JumpCondition.NotBelowOrEqual => t.Raw($"jnbe {label}"),
            _ => throw new ArgumentOutOfRangeException(nameof(condition), condition, null)
        };
    }
}
