global _native_io_write_byte
global _native_io_write_word
global _native_io_write_dword
global _native_io_read_byte
global _native_io_read_word
global _native_io_read_dword

section .text

; void out_byte(ushort port, byte value)
_native_io_write_byte:
    mov     dx, di
    mov     al, sil
    out     dx, al
    ret

; void out_word(ushort port, ushort value)
_native_io_write_word:
    mov     dx, di
    mov     ax, si
    out     dx, ax
    ret

; void out_dword(ushort port, uint value)
_native_io_write_dword:
    mov     dx, di
    mov     eax, esi
    out     dx, eax
    ret

; byte in_byte(ushort port)
_native_io_read_byte:
    mov     dx, di
    xor     eax, eax    ; Clear rax (xor eax,eax also zeros upper 32 bits)
    in      al, dx
    ret

; ushort in_word(ushort port)
_native_io_read_word:
    mov     dx, di
    xor     eax, eax    ; Clear rax
    in      ax, dx
    ret

; uint in_dword(ushort port)
_native_io_read_dword:
    mov     dx, di
    in      eax, dx
    ret
