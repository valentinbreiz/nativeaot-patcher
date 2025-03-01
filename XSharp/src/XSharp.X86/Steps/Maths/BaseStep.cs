using XSharp.Base;
using XSharp.Base.ControlFlow;
using XSharp.Base.Interfaces;
using XSharp.X86.Interfaces;
using XSharp.X86.Registers;
// ReSharper disable UnusedMethodReturnValue.Global
// ReSharper disable UnusedMember.Global

namespace XSharp.X86.Steps.Maths;

public static class BaseStepEx
{
    public static IX86 AddWithCarry(this IX86 self, IAddressable a, IAddressableOrConsonant b)
    {
        self.Raw($"adc {IAddressable.DoEmit(a)}, {IAddressableOrConsonant.DoEmit(b)}");
        return self;
    }

    public static IX86 Add(this IX86 self, IAddressable a, IAddressableOrConsonant b)
    {
        self.Raw($"add {IAddressable.DoEmit(a)}, {IAddressableOrConsonant.DoEmit(b)}");
        return self;
    }

    public static IX86 Subtract(this IX86 self, IAddressable a, IAddressableOrConsonant b)
    {
        self.Raw($"sub {IAddressable.DoEmit(a)}, {IAddressableOrConsonant.DoEmit(b)}");
        return self;
    }
}
