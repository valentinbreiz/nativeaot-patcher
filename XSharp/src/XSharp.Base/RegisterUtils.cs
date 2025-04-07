using XSharp.Base.Interfaces;

namespace XSharp.Base;

public static class RegisterUtils
{
    /// <summary>
    /// check if this register if in it
    /// </summary>
    /// <param name="r"></param>
    /// <param name="register"></param>
    /// <returns></returns>
    public static bool FitsIn<TName, TType>(IRegister<TName, TType> r, IRegister<TName, TType> register)
        where TName : Enum
        where TType : Enum =>
        r.Equals(register) ||
        r.InnerRegisters
            .Any(
                reg =>
                    reg.Register.Equals(register)
            );

    /// <summary>
    /// throws if the register fits in r
    /// </summary>
    /// <param name="r"></param>
    /// <param name="register"></param>
    /// <typeparam name="TName"></typeparam>
    /// <typeparam name="TType"></typeparam>
    /// <returns></returns>
    public static void FitsInThrow<TName, TType>(IRegister<TName, TType> r, IRegister<TName, TType> register)
        where TName : Enum
        where TType : Enum
    {
        if (FitsIn(r, register))
        {
            throw new ArgumentException("the register cant task up the same space", nameof(register));
        }
    }
}
