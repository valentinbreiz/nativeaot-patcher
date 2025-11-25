// ARM64 Exception Vector Table
// Follows same pattern as x64 Interrupts.asm

.global __arm64_exception_vectors
.global __arm64_init_exception_vectors

.extern __managed__irq

// ============================================================================
// Exception Vector Table - must be 2KB aligned for VBAR_EL1
// Each entry is 0x80 (128) bytes
// ============================================================================
.section .text
.balign 0x800
__arm64_exception_vectors:

// Current EL with SP0 (4 vectors)
.balign 0x80
    mov     x0, #0              // interrupt = SYNC (0)
    b       __exception_common
.balign 0x80
    mov     x0, #1              // interrupt = IRQ (1)
    b       __exception_common
.balign 0x80
    mov     x0, #2              // interrupt = FIQ (2)
    b       __exception_common
.balign 0x80
    mov     x0, #3              // interrupt = SERROR (3)
    b       __exception_common

// Current EL with SPx (kernel mode - this is what we use)
.balign 0x80
    mov     x0, #0
    b       __exception_common
.balign 0x80
    mov     x0, #1
    b       __exception_common
.balign 0x80
    mov     x0, #2
    b       __exception_common
.balign 0x80
    mov     x0, #3
    b       __exception_common

// Lower EL using AArch64
.balign 0x80
    mov     x0, #0
    b       __exception_common
.balign 0x80
    mov     x0, #1
    b       __exception_common
.balign 0x80
    mov     x0, #2
    b       __exception_common
.balign 0x80
    mov     x0, #3
    b       __exception_common

// Lower EL using AArch32 (not supported)
.balign 0x80
    b       .
.balign 0x80
    b       .
.balign 0x80
    b       .
.balign 0x80
    b       .

// ============================================================================
// Common exception handler
// On entry: x0 = interrupt type (0-3)
// Must build IRQContext struct matching C# layout:
//   x0-x30, sp, elr, spsr, interrupt, cpu_flags, far
//   Total: 37 fields × 8 = 296 bytes
// ============================================================================
__exception_common:
    // Save interrupt type in x9 (callee-saved)
    mov     x9, x0

    // Allocate stack for IRQContext (296 bytes)
    sub     sp, sp, #296

    // Save x0-x30 (offsets 0-240)
    // x0 will be saved later with correct value
    str     x1, [sp, #8]
    stp     x2, x3, [sp, #16]
    stp     x4, x5, [sp, #32]
    stp     x6, x7, [sp, #48]
    stp     x8, x9, [sp, #64]
    stp     x10, x11, [sp, #80]
    stp     x12, x13, [sp, #96]
    stp     x14, x15, [sp, #112]
    stp     x16, x17, [sp, #128]
    stp     x18, x19, [sp, #144]
    stp     x20, x21, [sp, #160]
    stp     x22, x23, [sp, #176]
    stp     x24, x25, [sp, #192]
    stp     x26, x27, [sp, #208]
    stp     x28, x29, [sp, #224]
    str     x30, [sp, #240]

    // x0 was clobbered, store 0 (we don't have original x0)
    str     xzr, [sp, #0]

    // Save sp (original sp before we modified it)
    add     x0, sp, #296
    str     x0, [sp, #248]          // sp at offset 248

    // Save elr_el1 (exception return address)
    mrs     x0, elr_el1
    str     x0, [sp, #256]          // elr at offset 256

    // Save spsr_el1
    mrs     x0, spsr_el1
    str     x0, [sp, #264]          // spsr at offset 264

    // Save interrupt type
    str     x9, [sp, #272]          // interrupt at offset 272

    // Save esr_el1 as cpu_flags
    mrs     x0, esr_el1
    str     x0, [sp, #280]          // cpu_flags at offset 280

    // Save far_el1 (fault address register)
    mrs     x0, far_el1
    str     x0, [sp, #288]          // far at offset 288

    // Call managed handler: __managed__irq(IRQContext* ctx)
    mov     x0, sp
    bl      __managed__irq

    // Restore elr_el1 and spsr_el1
    ldr     x0, [sp, #256]
    msr     elr_el1, x0
    ldr     x0, [sp, #264]
    msr     spsr_el1, x0

    // Restore x1-x30 (skip x0, we'll restore it last)
    ldr     x1, [sp, #8]
    ldp     x2, x3, [sp, #16]
    ldp     x4, x5, [sp, #32]
    ldp     x6, x7, [sp, #48]
    ldp     x8, x9, [sp, #64]
    ldp     x10, x11, [sp, #80]
    ldp     x12, x13, [sp, #96]
    ldp     x14, x15, [sp, #112]
    ldp     x16, x17, [sp, #128]
    ldp     x18, x19, [sp, #144]
    ldp     x20, x21, [sp, #160]
    ldp     x22, x23, [sp, #176]
    ldp     x24, x25, [sp, #192]
    ldp     x26, x27, [sp, #208]
    ldp     x28, x29, [sp, #224]
    ldr     x30, [sp, #240]

    // Restore x0 and deallocate stack
    ldr     x0, [sp, #0]
    add     sp, sp, #296

    eret

// ============================================================================
// Initialize exception vectors - set VBAR_EL1
// void __arm64_init_exception_vectors(void)
// ============================================================================
.balign 4
__arm64_init_exception_vectors:
    adrp    x0, __arm64_exception_vectors
    add     x0, x0, :lo12:__arm64_exception_vectors
    msr     vbar_el1, x0
    isb
    ret
