namespace XSharp.Base.Interfaces;

public interface IRegister<TName, TType>
    where TName : Enum
    where TType : Enum
{
    public TName Name { get; init; }
    public TType Size { get; init; }
    public string RegisterSize { get; }
    public List<IInnerRegister<TName, TType>> InnerRegisters { get; init; }
}

public interface IInnerRegister<TName, TType>
    where TName : Enum
    where TType : Enum
{
    public IRegister<TName, TType> Register { get; init; }
    public uint Start { get; init; }
    public uint End { get; init; }
}
