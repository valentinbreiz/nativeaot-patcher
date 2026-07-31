// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.InteropServices;

namespace Cosmos.Kernel.Core.SysCalls;

#if ARCH_ARM64

/// <summary>
/// Blittable register frame passed from the ARM64 SVC trap stub into the
/// managed syscall dispatcher. Mirrors the layout the native stub pushes on
/// entry (see Cosmos.Kernel.Native.ARM64 SVC stub); the only fields the
/// dispatcher reads are <see cref="Number"/> and <see cref="Arg0"/>..Arg5.
/// The remaining fields are captured for tracing / caller validation and
/// never interpreted by arch-neutral code.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct SysCallContext
{
    /// <summary>Syscall number (from x8 on AArch64).</summary>
    public SysCallNumber Number;

    /// <summary>Saved general-purpose argument registers (x0..x5).</summary>
    public ulong Arg0;
    public ulong Arg1;
    public ulong Arg2;
    public ulong Arg3;
    public ulong Arg4;
    public ulong Arg5;

    /// <summary>Calling user stack pointer (sp on entry).</summary>
    public ulong Sp;

    /// <summary>Exception link register — user return address (elr_el1).</summary>
    public ulong Elr;

    /// <summary>Saved program status register (spsr_el1).</summary>
    public ulong Spsr;

    /// <summary>Calling thread pointer, populated by the stub from the per-CPU
    /// scheduler state — never trusted from user-supplied registers.</summary>
    public void* Thread;
}

#else

/// <summary>
/// Blittable register frame passed from the x64 SYSCALL trap stub into the
/// managed syscall dispatcher. Mirrors the layout the native stub pushes on
/// entry (see Cosmos.Kernel.Native.X64 SYSCALL stub); the only fields the
/// dispatcher reads are <see cref="Number"/> and <see cref="Arg0"/>..Arg5.
/// The remaining fields are captured for tracing / caller validation and
/// never interpreted by arch-neutral code.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct SysCallContext
{
    /// <summary>Syscall number (from rax on x86-64).</summary>
    public SysCallNumber Number;

    /// <summary>Saved general-purpose argument registers (rdi, rsi, rdx, r10, r8, r9).</summary>
    public ulong Arg0;
    public ulong Arg1;
    public ulong Arg2;
    public ulong Arg3;
    public ulong Arg4;
    public ulong Arg5;

    /// <summary>Calling user stack pointer (rsp on entry, before the kernel transition).</summary>
    public ulong Rsp;

    /// <summary>Exception return address (rcx on SYSCALL; user RIP).</summary>
    public ulong Rip;

    /// <summary>Saved RFLAGS (r11 on SYSCALL).</summary>
    public ulong Rflags;

    /// <summary>Calling thread pointer, populated by the stub from the per-CPU
    /// scheduler state — never trusted from user-supplied registers.</summary>
    public void* Thread;
}

#endif
