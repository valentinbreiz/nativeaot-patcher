; Write barrier implementations for x64

BITS 64

section .text

; RhpByRefAssignRef - Copy one reference field with write barrier
; Input:  RDI = pointer to destination
;         RSI = pointer to source
; Output: RDI advanced by 8, RSI advanced by 8
; ILC expects this to copy 8 bytes from [RSI] to [RDI] and advance both pointers
global RhpByRefAssignRef
RhpByRefAssignRef:
    movsq                    ; Copy 8 bytes: [RDI] = [RSI], then RDI+=8, RSI+=8
    ret
