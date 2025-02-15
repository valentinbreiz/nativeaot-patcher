using XSharp.Base.ControlFlow;
using XSharp.X86.Interfaces;

namespace XSharp.X86.Steps;

public static class ReturnEx
{

    public static T Return<T>(this T x86)
        where T : IX86
    {
        return x86.Raw("ret");
    }

}
