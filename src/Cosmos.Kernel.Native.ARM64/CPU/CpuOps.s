.global _native_cpu_halt
.global _native_cpu_memory_barrier
.global __arm64_disable_alignment_check

.text
.align 4

_native_cpu_halt:
    wfi                     // Wait for interrupt
    ret

_native_cpu_memory_barrier:
    dmb     sy              // Data memory barrier - system
    ret

// Disable alignment checking in SCTLR_EL1
// Clears the A bit (bit 1) to allow unaligned accesses
__arm64_disable_alignment_check:
    mrs     x0, sctlr_el1       // Read SCTLR_EL1
    bic     x0, x0, #(1 << 1)   // Clear A bit (alignment check)
    msr     sctlr_el1, x0       // Write back
    isb                         // Instruction synchronization barrier
    ret
