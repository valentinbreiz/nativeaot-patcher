global _native_io_write_byte
global _native_io_read_byte

section .text

; void out_byte(ushort port, byte value)
_native_io_write_byte:
    mov     dx, di
    mov     al, sil
    out     dx, al
    ret

; byte in_byte(ushort port)
_native_io_read_byte:
    mov     dx, di
    in      al, dx
    ret