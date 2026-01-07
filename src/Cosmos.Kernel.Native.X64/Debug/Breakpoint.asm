global _native_debug_breakpoint
global _native_debug_breakpoint_soft

section .text

; void breakpoint()
; Triggers a software breakpoint (INT3) that GDB can catch
_native_debug_breakpoint:
    int3        ; Software breakpoint instruction
    nop         ; Add a NOP after int3 to help GDB step over
    ret

; void breakpoint_soft()
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
