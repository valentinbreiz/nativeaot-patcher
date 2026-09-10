// ARM64 SVC (syscall) trap path - routes SVC exceptions from EL0 into the
// managed SysCallNative.Dispatch entry (__managed__syscall). Mirrors the IRQ
// path in CPU/Interrupts.s.
//
// Entry: the Lower-EL AArch64 SYNC vector slot (in CPU/Interrupts.s) reads
// ESR_EL1, and on EC == 0x15 (SVC from AArch64) branches here with a 48-byte
// frame already pushed:
//   [sp+0x00] x8   (syscall number)
//   [sp+0x08] x30  (user link register)
//   [sp+0x10] x6
//   [sp+0x18] x7
//   [sp+0x20] x4
//   [sp+0x28] x5
//   [sp+0x30] x2
//   [sp+0x38] x3
//   [sp+0x40] x0   (syscall arg0 / return register)
//   [sp+0x48] x1   (syscall arg1)
//
// SysCallContext frame built here (natural 8-byte alignment, matches the C#
// layout in src/Cosmos.Kernel.Core/SysCalls/SysCallContext.cs):
//   +0x00  uint  Number  (x8, 32-bit)
//   +0x08  ulong Arg0     (x0)
//   +0x10  ulong Arg1     (x1)
//   +0x18  ulong Arg2     (x2)
//   +0x20  ulong Arg3     (x3)
//   +0x28  ulong Arg4     (x4)
//   +0x30  ulong Arg5     (x5)
//   +0x38  ulong Sp       (sp_el0, user stack)
//   +0x40  ulong Elr      (elr_el1, user return PC)
//   +0x48  ulong Spsr     (spsr_el1, user PSTATE)
//   +0x50  void*  Thread  (0 - C# resolves via SchedulerManager)
//   total: 88 bytes, padded to 96 for 16-byte alignment before the call.
//
// Register preservation: AAPCS caller-saved x0-x18 are saved across the
// managed call (x0-x8 & x30 in the vector frame, x9-x18 in a dedicated save
// area); x19-x28 are callee-saved and preserved by the managed dispatcher.
// x0 carries the packed long result back to userland; all other GPRs are
// restored. FP/NEON state is not saved here - syscalls are synchronous
// callers and can declare V registers clobbered in the syscall ABI until
// the FP-preservation pass lands (parity with the IRQ path's Q save).

.text

.extern __managed__syscall

// ----------------------------------------------------------------------------
// __syscall_common - SVC handler. See file header for the on-entry frame.
// ----------------------------------------------------------------------------
.global __syscall_common
__syscall_common:
    // ----- save caller-saved x9-x18 across the managed call (80 bytes) -----
    sub     sp, sp, #96
    stp     x9, x10, [sp, #0]
    stp     x11, x12, [sp, #16]
    stp     x13, x14, [sp, #32]
    stp     x15, x16, [sp, #48]
    stp     x17, x18, [sp, #64]

    // ----- build SysCallContext (88 bytes; padded to 96 for 16B align) -----
    sub     sp, sp, #96
    add     x9, sp, #192            // x9 = vector frame base (sp + save(96) + ctx(96))

    ldr     w10, [x9, #0]          // x8 = syscall number
    str     w10, [sp, #0]          // Number
    ldr     x10, [x9, #64]         // x0 -> Arg0
    str     x10, [sp, #8]
    ldr     x10, [x9, #72]         // x1 -> Arg1
    str     x10, [sp, #16]
    ldr     x10, [x9, #48]         // x2 -> Arg2
    str     x10, [sp, #24]
    ldr     x10, [x9, #56]         // x3 -> Arg3
    str     x10, [sp, #32]
    ldr     x10, [x9, #32]         // x4 -> Arg4
    str     x10, [sp, #40]
    ldr     x10, [x9, #40]         // x5 -> Arg5
    str     x10, [sp, #48]
    mrs     x10, sp_el0
    str     x10, [sp, #56]         // Sp (user stack)
    mrs     x10, elr_el1
    str     x10, [sp, #64]         // Elr (user return PC)
    mrs     x10, spsr_el1
    str     x10, [sp, #72]         // Spsr (user PSTATE)
    str     xzr, [sp, #80]         // Thread = 0 (C# resolves the thread)

    mov     x0, sp                 // first arg = &SysCallContext
    bl      __managed__syscall     // returns packed long result in x0

    // ----- return to user -----
    add     sp, sp, #96            // drop context (+ padding) -> save area

    // restore x9-x18 (x0 = result, untouched)
    ldp     x9, x10, [sp, #0]
    ldp     x11, x12, [sp, #16]
    ldp     x13, x14, [sp, #32]
    ldp     x15, x16, [sp, #48]
    ldp     x17, x18, [sp, #64]

    add     sp, sp, #96            // drop save area -> vector frame base

    // restore user x1-x8, x6-x7, x30 from the vector frame; keep x0 = result
    ldr     x30, [sp, #8]
    ldr     x8, [sp, #0]
    ldp     x6, x7, [sp, #16]
    ldp     x4, x5, [sp, #32]
    ldp     x2, x3, [sp, #48]
    ldr     x1, [sp, #72]

    add     sp, sp, #48            // drop the vector frame
    eret                             // elr_el1 -> PC, spsr_el1 -> PSTATE