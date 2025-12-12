global _native_cpu_halt
global _native_cpu_memory_barrier
global _native_x64_get_rsp
global _native_x64_get_rbp

section .text

_native_cpu_halt:
    hlt
    ret

_native_cpu_memory_barrier:
    mfence                  ; Memory fence - serialize all loads/stores
    ret

; Get the current stack pointer (RSP)
; Returns: RSP value in RAX
_native_x64_get_rsp:
    mov rax, rsp
    ret

; Get the current base pointer (RBP)
; Returns: RBP value in RAX
_native_x64_get_rbp:
    mov rax, rbp
    ret
