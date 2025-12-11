.global _native_cpu_halt
.global _native_cpu_memory_barrier
.global __arm64_disable_alignment_check
.global __arm64_enable_neon

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

// Enable NEON/FP (SIMD) access
// Sets CPACR_EL1.FPEN bits [21:20] to 0b11 to enable FP/SIMD at EL1 and EL0
__arm64_enable_neon:
    mrs     x0, cpacr_el1       // Read CPACR_EL1
    orr     x0, x0, #(3 << 20)  // Set FPEN bits [21:20] = 0b11 (full access)
    msr     cpacr_el1, x0       // Write back
    isb                         // Instruction synchronization barrier
    ret
