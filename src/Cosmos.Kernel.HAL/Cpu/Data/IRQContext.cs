// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.InteropServices;
using Cosmos.Kernel.HAL.Cpu.Enums;

namespace Cosmos.Kernel.HAL.Cpu.Data;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct IRQContext
{
    public uint interrupt;
    public ulong rax;
    public ulong rcx;
    public ulong rdx;
    public ulong rbx;
    public ulong rbp;
    public ulong rsi;
    public ulong rdi;
    public ulong r8;
    public ulong r9;
    public ulong r10;
    public ulong r11;
    public ulong r12;
    public ulong r13;
    public ulong r14;
    public uint r15;

}
