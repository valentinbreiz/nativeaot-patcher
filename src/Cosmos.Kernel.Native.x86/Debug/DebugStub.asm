global _native_debug_stub
global _native_debug_breakpoint_soft

section .text

; void debug_breakpoint_soft()
; A softer breakpoint that can be stepped over
_native_debug_breakpoint_soft:
    push rbp
    mov rbp, rsp
    ; If debugger is attached, it will stop here naturally
    nop             ; GDB breakpoint location
    nop
    nop
    pop rbp
    ret