.global _native_cpu_halt
.global _native_cpu_memory_barrier
.global _native_cpu_disable_interrupts
.global _native_cpu_enable_interrupts

.text
.align 4

_native_cpu_halt:
    wfi                     // Wait for interrupt
    ret

_native_cpu_memory_barrier:
    dmb     sy              // Data memory barrier - system
    ret

// Disable interrupts by setting DAIF.I and DAIF.F bits
_native_cpu_disable_interrupts:
    msr     daifset, #3     // Set I and F bits (disable IRQ and FIQ)
    ret

// Enable interrupts by clearing DAIF.I and DAIF.F bits
_native_cpu_enable_interrupts:
    msr     daifclr, #3     // Clear I and F bits (enable IRQ and FIQ)
    ret
