namespace XSharp.X86;

public enum JumpCondition
{
    Zero = 0,
    Equal,
    NotZero,
    NotEqual,
    Carry,
    NotCarry,
    Overflow,
    NotOverflow,
    Signed,
    NotSigned,
    Parity,
    ParityIsEven,
    ParityIsOdd,
    NotParity,

    CxIsZero,
    EcxIsZero,

    Greater,
    NotGreater,
    LessOrEqual,
    NotLessOrEqual,
    GreaterOrEqual,
    NotGreaterOrEqual,
    Less,
    NotLess,

    Above,
    NotAbove,
    AboveOrEqual,
    NotAboveOrEqual,
    Below,
    NotBelow,
    BelowOrEqual,
    NotBelowOrEqual,

}
