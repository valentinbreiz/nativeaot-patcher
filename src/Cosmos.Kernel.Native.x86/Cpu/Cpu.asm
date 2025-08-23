section .text

; Load Interrupt Descriptor Table
; void _native_lidt(void* baseAddress, ushort size)
; rdi = base address, rsi = size

global _native_lidt
_native_lidt:
    sub     rsp, 16            ; allocate space for descriptor
    mov     word [rsp], si     ; store size
    mov     qword [rsp+2], rdi ; store base
    lidt    [rsp]              ; load IDT
    add     rsp, 16            ; clean stack
    ret

; Enable interrupts
; void _native_sti()

global _native_sti
_native_sti:
    sti
    ret
