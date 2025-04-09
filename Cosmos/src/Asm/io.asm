global _native_io_write_byte
global _native_io_write_word
global _native_io_write_dword
global _native_io_write_qword

global _native_io_read_byte
global _native_io_read_word
global _native_io_read_dword
global _native_io_read_qword

section .text

; void out_byte(ushort port, byte value)
_native_io_write_byte:
    push rdx
    push rax 
    mov     dx, di
    mov     al, sil
    out     dx, al
    pop     rax
    pop     rdx
    ret

_native_io_write_word:
    push rdx
    push rax 
    mov     dx, di
    mov     ax, si
    out     dx, ax
    pop rax
    pop rdx
    ret

; void out_dword(ushort port, uint value)
_native_io_write_dword:
    push rdx
    push rax 
    mov     dx, di
    mov     eax, esi
    out     dx, eax
    pop     rax
    pop     rdx
    ret

; byte in_byte(ushort port)
_native_io_read_byte:
    push rdx
    mov     dx, di
    in      al, dx
    pop     rdx
    ret

; ushort in_word(ushort port)
_native_io_read_word:
    push rdx
    mov     dx, di
    in      ax, dx
    pop     rdx
    ret

; uint in_dword(ushort port)
_native_io_read_dword:
    push rdx
    mov     dx, di
    in      eax, dx
    pop     rdx
    ret