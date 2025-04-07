// This code is licensed under MIT license (see LICENSE for details)

using XSharp.Base.ControlFlow;
using XSharp.X86.Interfaces;
using XSharp.X86.Registers;

namespace XSharp.X86.Steps;

public static class NoopEx
{
    public static T Noop<T>(this T x)
        where T : IX86 =>
        x.Raw("nop");
}
