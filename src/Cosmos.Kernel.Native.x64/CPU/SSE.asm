global EnableSSE

section .text

; Enable SSE support for x86-64
EnableSSE:
    ; Read CR0 register
    mov rax, cr0
    and rax, ~(1 << 2)  ; Clear EM (bit 2) - Emulation
    or  rax, (1 << 1)   ; Set MP (bit 1) - Monitor co-processor
    mov cr0, rax

    ; Read CR4 register
    mov rax, cr4
    or  rax, (3 << 9)   ; Set OSFXSR (bit 9) and OSXMMEXCPT (bit 10)
    mov cr4, rax

    ret