global com_init
global com_write

section .text

%define COM1_PORT 0x3F8

; void com_init()
com_init:
    push rax
    push rdx
    mov dx, COM1_PORT + 1
    mov al, 0x00
    out dx, al
    mov dx, COM1_PORT + 3
    mov al, 0x80
    out dx, al
    mov dx, COM1_PORT
    mov al, 0x01
    out dx, al
    mov dx, COM1_PORT + 1
    mov al, 0x00
    out dx, al
    mov dx, COM1_PORT + 3
    mov al, 0x03
    out dx, al
    mov dx, COM1_PORT + 2
    mov al, 0xC7
    out dx, al
    pop rdx
    pop rax
    ret

com_write:
    push rax
    push rdx

.wait_loop:
    mov dx, COM1_PORT + 5
    in al, dx
    test al, 0x20
    jz .wait_loop
    
    mov dx, COM1_PORT
    mov al, dil
    out dx, al
    
    pop rdx
    pop rax
    ret