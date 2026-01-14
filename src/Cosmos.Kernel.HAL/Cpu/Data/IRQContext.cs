// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.InteropServices;

namespace Cosmos.Kernel.HAL.Cpu.Data;

#if ARCH_ARM64

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct IRQContext
{
    // General purpose registers x0-x30
    public ulong x0;
    public ulong x1;
    public ulong x2;
    public ulong x3;
    public ulong x4;
    public ulong x5;
    public ulong x6;
    public ulong x7;
    public ulong x8;
    public ulong x9;
    public ulong x10;
    public ulong x11;
    public ulong x12;
    public ulong x13;
    public ulong x14;
    public ulong x15;
    public ulong x16;
    public ulong x17;
    public ulong x18;
    public ulong x19;
    public ulong x20;
    public ulong x21;
    public ulong x22;
    public ulong x23;
    public ulong x24;
    public ulong x25;
    public ulong x26;
    public ulong x27;
    public ulong x28;
    public ulong x29;  // Frame pointer
    public ulong x30;  // Link register

    public ulong sp;   // Stack pointer
    public ulong elr;  // Exception link register (return address)
    public ulong spsr; // Saved program status register

    public ulong interrupt;  // Exception type (0=sync, 1=irq, 2=fiq, 3=serror)
    public ulong cpu_flags;  // ESR_EL1 (exception syndrome)
    public ulong far;        // FAR_EL1 (fault address for data/instruction aborts)
}

#else

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
    public ulong cr2;  // Page fault linear address (valid for #PF, int 14)
}

#endif
