using XSharp.Base;
using XSharp.Base.ControlFlow;
using XSharp.Base.Interfaces;
using XSharp.X86.Interfaces;
using XSharp.X86.Registers;

namespace XSharp.X86.Steps.Maths;

public static class AddStepEx {


    public static IX86 Add(this IX86 self, IAddressable a, IAddressableOrConsonant b)
    {
        self.Raw($"add {IAddressable.DoEmit(a)}, {IAddressableOrConsonant.DoEmit(b)}");
        return self;
    }


}
