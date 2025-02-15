using XSharp.Base.ControlFlow;
using XSharp.X86.Interfaces;
using XSharp.X86.Registers;

namespace XSharp.X86.ControlFlow;

public static class ReturnEx
{

    public static IX86 Return(this IX86 x86)
    {
        return x86.Raw("ret");
    }

}
