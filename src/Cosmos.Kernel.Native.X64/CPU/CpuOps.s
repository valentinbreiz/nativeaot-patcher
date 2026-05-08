.intel_syntax noprefix

.global _native_cpu_halt
.global _native_cpu_rdtsc
.global _native_cpu_disable_interrupts
.global _native_cpu_enable_interrupts
.global _native_cpu_save_irq_and_disable
.global _native_cpu_restore_irq

.text

_native_cpu_halt:
    hlt
    ret

// Disable interrupts (CLI)
_native_cpu_disable_interrupts:
    cli
    ret

// Enable interrupts (STI)
_native_cpu_enable_interrupts:
    sti
    ret

// Save current RFLAGS (including the IF bit) and disable interrupts.
// Returns the saved RFLAGS in RAX so a later _native_cpu_restore_irq
// call can restore the exact prior interrupt-enable state. Used by
// InterruptScope to make nested cli/sti safe.
_native_cpu_save_irq_and_disable:
    pushfq
    pop rax
    cli
    ret

// Restore RFLAGS (from RDI under the System V x86-64 ABI) — flips IF
// back to whatever state it was in when _native_cpu_save_irq_and_disable
// was called.
_native_cpu_restore_irq:
    push rdi
    popfq
    ret

// Read Time Stamp Counter
// Returns: 64-bit TSC value in RAX
_native_cpu_rdtsc:
    rdtsc                   // EDX:EAX = timestamp counter
    shl     rdx, 32         // Shift high 32 bits to upper half of RDX
    or      rax, rdx        // Combine into RAX (return value)
    ret
