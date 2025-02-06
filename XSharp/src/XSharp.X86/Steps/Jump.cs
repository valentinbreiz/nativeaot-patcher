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
}
