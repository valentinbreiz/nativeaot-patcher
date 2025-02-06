// This code is licensed under MIT license (see LICENSE for details)

using XSharp.Base.ControlFlow;
using XSharp.X86.ContorlFlow;
using XSharp.X86.Interfaces;

namespace XSharp.X86.Steps;

public static class GroupEx
{

    public static IX86 Group(this IX86 x86, Action<IX86> action)
    {
        action(x86);
        return x86;
    }

}
