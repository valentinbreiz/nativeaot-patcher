using XSharp.Base.ControlFlow;
using XSharp.X86.Interfaces;

namespace XSharp.X86.Steps;

public static class IfEx
{
    public static T If<T>(this T x86, JumpCondition condition, Action<IX86> actionTrue, Action<IX86> actionFalse)
        where T : IX86
    {
        LabelObject? baseLabel = x86.GetNext();
        LabelObject? trueLabel = LabelObject.Get($"{baseLabel}__true");
        LabelObject? falseLabel = LabelObject.Get($"{baseLabel}__false");
        LabelObject? endLabel = LabelObject.Get($"{baseLabel}__end");

        x86
            .Jump(trueLabel, condition)
            .Label(falseLabel)
            .Group(i => { actionFalse?.Invoke(i); })
            .Jump(endLabel)
            .Label(trueLabel)
            .Raw("")
            .Group(i => { actionTrue?.Invoke(i); })
            .Label(endLabel)
            .Raw("");

        return x86;
    }
}
