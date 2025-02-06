// This code is licensed under MIT license (see LICENSE for details)

using XSharp.Base.Interfaces;

namespace XSharp.X86.Registers;

public class X86Register: IRegister<X86Registers, X86RegisterSize>
{

    public X86Register(X86Registers name, X86RegisterSize size)
    {
        Name = name;
        Size = size;
        InnerRegisters = [];
    }

    public X86Register(X86Registers name, X86RegisterSize size, params X86InnerRegister[] innerRegisters)
    {
        Name = name;
        Size = size;
        InnerRegisters = new List<IInnerRegister<X86Registers, X86RegisterSize>>(innerRegisters.ToList());
    }

    public X86Registers Name { get; init; }
    public X86RegisterSize Size { get; init; }
    public string RegisterSize {
        get
        {
            return Size switch
            {
                X86RegisterSize.Bit8 => "byte",
                X86RegisterSize.Bit16 => "word",
                X86RegisterSize.Bit32 => "dword",
                X86RegisterSize.Bit64 => "qword",
                X86RegisterSize.Bit128 or X86RegisterSize.Bit256 or X86RegisterSize.Bit512 =>
                    throw new NotSupportedException("Unknown register size"),
                _ => throw new NotSupportedException("Unknown register size")
            };
        }
    }
    public List<IInnerRegister<X86Registers, X86RegisterSize>> InnerRegisters { get; init; }

    public override bool Equals(object? obj)
    {
        if(obj is not X86Register register) return false;

        return register.Size == Size && register.Name == Name;
    }

    public override string ToString() => Name.ToString();
}

public class X86InnerRegister : IInnerRegister<X86Registers, X86RegisterSize>
{

    public X86InnerRegister(IRegister<X86Registers, X86RegisterSize> register, uint start, uint end)
    {
        Register = register;
        Start = start;
        End = end;
    }

    public IRegister<X86Registers, X86RegisterSize> Register { get; init; }
    public uint Start { get; init; }
    public uint End { get; init; }
}
