global _native_cpu_halt
global _native_cpu_memory_barrier
global _native_cpu_rdtsc

section .text

_native_cpu_halt:
    hlt
    ret

_native_cpu_memory_barrier:
    mfence                  ; Memory fence - serialize all loads/stores
    ret

; Read Time Stamp Counter
; Returns: 64-bit TSC value in RAX
_native_cpu_rdtsc:
    rdtsc                   ; EDX:EAX = timestamp counter
    shl     rdx, 32         ; Shift high 32 bits to upper half of RDX
    or      rax, rdx        ; Combine into RAX (return value)
    ret
