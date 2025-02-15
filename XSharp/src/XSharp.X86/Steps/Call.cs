using XSharp.Base.ControlFlow;
using XSharp.X86.Interfaces;

namespace XSharp.X86.Steps;

public static class CallEx
{
    public static T Call<T>(this T x86, LabelObject label)
        where T : IX86
    {
        return x86.Raw($"call {label}");
    }
}
