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
        where TType : Enum
    {
        return r.Equals(register) ||
               r.InnerRegisters
                   .Any(
                       reg =>
                           reg.Register.Equals(register)
                   );
    }
}

