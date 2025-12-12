.global _native_cpu_halt
.global _native_cpu_memory_barrier
.global _native_arm64_get_sp
.global _native_arm64_get_fp

.text
.align 4

_native_cpu_halt:
    wfi                     // Wait for interrupt
    ret

_native_cpu_memory_barrier:
    dmb     sy              // Data memory barrier - system
    ret

// Get the current stack pointer (SP)
// Returns: SP value in x0
_native_arm64_get_sp:
    mov     x0, sp
    ret

// Get the current frame pointer (FP/x29)
// Returns: FP value in x0
_native_arm64_get_fp:
    mov     x0, x29
    ret
