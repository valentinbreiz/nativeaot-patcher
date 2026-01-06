global _native_cpu_halt
global _native_cpu_rdtsc
global _native_cpu_disable_interrupts
global _native_cpu_enable_interrupts

section .text

_native_cpu_halt:
    hlt
    ret

; Disable interrupts (CLI)
_native_cpu_disable_interrupts:
    cli
    ret

; Enable interrupts (STI)
_native_cpu_enable_interrupts:
    sti
    ret

; Read Time Stamp Counter
; Returns: 64-bit TSC value in RAX
_native_cpu_rdtsc:
    rdtsc                   ; EDX:EAX = timestamp counter
    shl     rdx, 32         ; Shift high 32 bits to upper half of RDX
    or      rax, rdx        ; Combine into RAX (return value)
    ret
