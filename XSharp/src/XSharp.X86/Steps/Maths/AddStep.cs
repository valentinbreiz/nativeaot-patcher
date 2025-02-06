using XSharp.Base.ControlFlow;
using XSharp.Base.Interfaces;
using XSharp.X86.Interfaces;
using XSharp.X86.Registers;

namespace XSharp.X86.Steps.Maths;

public static class AddStepEx {
    public static IX86 Add(this IX86 self, X86Register a, X86Register b)
    {
        self.Raw($"add {a}, {b}");
        return self;
    }

    public static IX86 Add(this IX86 self, X86Register a, X86Pointer b)
    {
        self.Raw($"add {a}, {b}");
        return self;
    }

    public static IX86 Add(this IX86 self, X86Register a, int b)
    {

        self.Raw($"add {a}, {b}");
        return self;
    }

}
