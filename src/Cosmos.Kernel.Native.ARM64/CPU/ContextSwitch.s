// ARM64 Context Switch Support
// Functions for preemptive multithreading on ARM64

.global _native_arm64_set_context_switch_sp
.global _native_arm64_get_context_switch_sp
.global _native_arm64_get_sp
.global _native_arm64_set_context_switch_new_thread
.global _context_switch_target_sp
.global _context_switch_is_new_thread
.global _temp_is_new_thread

.section .bss

// Per-CPU context switch target SP (0 = no switch, non-zero = switch to this SP)
// For SMP, this would be per-CPU, but for now single-CPU
.balign 8
_context_switch_target_sp:
    .quad 0

// Flag indicating if the target thread is NEW (1) or RESUMED (0)
// NEW threads need SP loaded and branch to entry point
// RESUMED threads use eret
.balign 8
_context_switch_is_new_thread:
    .quad 0

// Temporary storage for is_new_thread flag during restore
.balign 8
_temp_is_new_thread:
    .quad 0

.section .text

// void _native_arm64_set_context_switch_sp(nuint newSp)
// Sets the target SP for context switch. Called from managed code
// during timer interrupt to request a context switch.
// x0 = new SP to switch to (pointing to saved context)
.balign 4
_native_arm64_set_context_switch_sp:
    adrp    x1, _context_switch_target_sp
    add     x1, x1, :lo12:_context_switch_target_sp
    str     x0, [x1]
    ret

// nuint _native_arm64_get_context_switch_sp(void)
// Gets the current context switch target SP (for debugging)
.balign 4
_native_arm64_get_context_switch_sp:
    adrp    x1, _context_switch_target_sp
    add     x1, x1, :lo12:_context_switch_target_sp
    ldr     x0, [x1]
    ret

// nuint _native_arm64_get_sp(void)
// Gets the current SP value
.balign 4
_native_arm64_get_sp:
    mov     x0, sp
    ret

// void _native_arm64_set_context_switch_new_thread(int isNew)
// Sets whether the target thread is NEW (1) or RESUMED (0)
// x0 = isNew flag
.balign 4
_native_arm64_set_context_switch_new_thread:
    adrp    x1, _context_switch_is_new_thread
    add     x1, x1, :lo12:_context_switch_is_new_thread
    str     x0, [x1]
    ret
