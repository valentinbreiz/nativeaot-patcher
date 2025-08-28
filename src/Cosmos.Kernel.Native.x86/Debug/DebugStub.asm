global _native_debug_stub
global _native_debug_breakpoint_soft

section .text

; void debug_stub()
; A simple stub function that GDB can use as a breakpoint location
; This allows stepping over without issues
_native_debug_stub:
    push rbp
    mov rbp, rsp
    nop             ; GDB can set a breakpoint here
    nop             ; Multiple NOPs for safety
    nop
    pop rbp
    ret

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