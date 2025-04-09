global com_write
section .text

; void com_write(uint8_t value)
com_write:
    mov dx, 0x3F8        ; Port COM1
    mov al, dil          ; First argument
    out dx, al           ; Write AL -> port
    ret
