global _native_cpu_halt
global _native_cpu_memory_barrier

section .text

_native_cpu_halt:
    hlt
    ret

_native_cpu_memory_barrier:
    mfence                  ; Memory fence - serialize all loads/stores
    ret
