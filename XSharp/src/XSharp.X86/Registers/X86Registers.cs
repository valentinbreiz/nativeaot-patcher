// ReSharper disable InconsistentNaming

namespace XSharp.X86.Registers;

public enum X86Registers
{
    AH,
    AL,
    BH,
    BL,
    CH,
    CL,
    DH,
    DL,

    AX,
    BX,
    CX,
    DX,

    EAX,
    EBX,
    ECX,
    EDX,
    ESP,
    EBP,
    ESI,
    EDI,

    RAX,
    RBX,
    RCX,
    RDX,
    RSP,
    RBP,
    RSI,
    RDI
}

public enum X86RegisterSize
{
    SP = 0,
    Bit8 = 8,
    Bit16 = 16,
    Bit32 = 32,
    Bit64 = 64,
    Bit128 = 128,
    Bit256 = 256,
    Bit512 = 512
}
