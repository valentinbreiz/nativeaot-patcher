using XSharp.X86.Interfaces;
using XSharp.X86.Registers;

namespace XSharp.X86;

public class X86 : Base.XSharp, IX86
{
    public static IX86 New() => new X86();

    // ReSharper disable InconsistentNaming
    // ReSharper disable ArrangeObjectCreationWhenTypeNotEvident

    public static X86Register SP => new(X86Registers.ESP, X86RegisterSize.Bit8);

    public static X86Register AH => new(X86Registers.AH, X86RegisterSize.Bit8);
    public static X86Register AL => new(X86Registers.AL, X86RegisterSize.Bit8);
    public static X86Register AX => new(X86Registers.AX, X86RegisterSize.Bit16, new(AH, 0, 7), new(AL, 8, 15));

    public static X86Register EAX => new(X86Registers.EAX, X86RegisterSize.Bit32,
        new(AH, 0, 7),
        new(AL, 8, 15),
        new(AX, 0, 15));

    public static X86Register RAX => new(X86Registers.RAX, X86RegisterSize.Bit32,
        new(AH, 0, 7),
        new(AL, 8, 15),
        new(AX, 0, 15),
        new(EAX, 0, 15));

    public static X86Register BH => new(X86Registers.BH, X86RegisterSize.Bit8);
    public static X86Register BL => new(X86Registers.BL, X86RegisterSize.Bit8);
    public static X86Register BX => new(X86Registers.BX, X86RegisterSize.Bit16, new(BH, 0, 7), new(BL, 8, 15));

    public static X86Register EBX => new(X86Registers.EBX, X86RegisterSize.Bit32,
        new(BH, 0, 7),
        new(BL, 8, 15),
        new(BX, 0, 15));

    public static X86Register RBX => new(X86Registers.RBX, X86RegisterSize.Bit64,
        new(BH, 0, 7),
        new(BL, 8, 15),
        new(BX, 0, 15),
        new(EBX, 0, 15));

    public static X86Register CH => new(X86Registers.CH, X86RegisterSize.Bit8);
    public static X86Register CL => new(X86Registers.CL, X86RegisterSize.Bit8);
    public static X86Register CX => new(X86Registers.CX, X86RegisterSize.Bit16, new(CH, 0, 7), new(CL, 8, 15));

    public static X86Register ECX => new(X86Registers.ECX, X86RegisterSize.Bit32,
        new(CH, 0, 7),
        new(CL, 8, 15),
        new(CX, 0, 15));

    public static X86Register RCX => new(X86Registers.RCX, X86RegisterSize.Bit64,
        new(CH, 0, 7),
        new(CL, 8, 15),
        new(CX, 0, 15),
        new(ECX, 0, 15));

    public static X86Register DH => new(X86Registers.DH, X86RegisterSize.Bit8);
    public static X86Register DL => new(X86Registers.DL, X86RegisterSize.Bit8);
    public static X86Register DX => new(X86Registers.DX, X86RegisterSize.Bit16, new(DH, 0, 7), new(DL, 8, 15));

    public static X86Register EDX => new(X86Registers.EDX, X86RegisterSize.Bit32,
        new(DH, 0, 7),
        new(DL, 8, 15),
        new(DX, 0, 15));

    public static X86Register RDX => new(X86Registers.RDX, X86RegisterSize.Bit64,
        new(DH, 0, 7),
        new(DL, 8, 15),
        new(DX, 0, 15),
        new(EDX, 0, 15));

    // ReSharper restore ArrangeObjectCreationWhenTypeNotEvident
    // ReSharper restore InconsistentNaming
}
