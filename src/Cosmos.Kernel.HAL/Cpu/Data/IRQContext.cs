// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.InteropServices;
using Cosmos.Kernel.HAL.Cpu.Enums;

namespace Cosmos.Kernel.HAL.Cpu.Data;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct IRQContext
{
    public ulong r15;
    public ulong r14;
    public ulong r13;
    public ulong r12;
    public ulong r11;
    public ulong r10;
    public ulong r9;
    public ulong r8;
    public ulong rdi;
    public ulong rsi;
    public ulong rbp;
    public ulong rbx;
    public ulong rdx;
    public ulong rcx;
    public ulong rax;
    public ulong interrupt;
    public ulong cpu_flags;

}
