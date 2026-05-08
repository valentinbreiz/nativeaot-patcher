.global _native_cpu_halt
.global _native_cpu_disable_interrupts
.global _native_cpu_enable_interrupts
.global _native_cpu_save_irq_and_disable
.global _native_cpu_restore_irq

.text
.align 4

_native_cpu_halt:
    wfi                     // Wait for interrupt
    ret

// Disable interrupts by setting DAIF.I and DAIF.F bits
_native_cpu_disable_interrupts:
    msr     daifset, #3     // Set I and F bits (disable IRQ and FIQ)
    ret

// Enable interrupts by clearing DAIF.I and DAIF.F bits
_native_cpu_enable_interrupts:
    msr     daifclr, #3     // Clear I and F bits (enable IRQ and FIQ)
    ret

// Save current DAIF (including I and F masks) and disable IRQ + FIQ.
// Returns the saved DAIF in x0 so a later _native_cpu_restore_irq call
// can restore the exact prior masking state. Used by InterruptScope to
// make nested mask/unmask safe.
_native_cpu_save_irq_and_disable:
    mrs     x0, daif
    msr     daifset, #3
    ret

// Restore DAIF (from x0) — flips I/F back to whatever state they were
// in when _native_cpu_save_irq_and_disable was called.
_native_cpu_restore_irq:
    msr     daif, x0
    ret
