namespace XSharp.X86.Interfaces;

public interface IAddressable
{
    public static string DoEmit(IAddressable item)
    {
        if (item is IPointer pointer)
        {
            return pointer.Emit();
        }
        else if (item is IRegisterArg register)
        {
            return register.Emit();
        }

        throw new NotImplementedException();
    }
}

public interface IAddressableOrConsonant
{
    public static string DoEmit(IAddressableOrConsonant item)
    {
        if (item is IAddressable address)
        {
            return IAddressable.DoEmit(address);
        }
        else if (item is IConstant consonant)
        {
            return consonant.Emit();
        }

        throw new NotImplementedException();
    }
}

public interface IConstant : IAddressableOrConsonant
{
    public string Emit();
}

public interface IPointer : IAddressable, IAddressableOrConsonant
{
    public string Emit();
}

public interface IRegisterArg : IAddressable, IAddressableOrConsonant
{
    public string Emit();
}
