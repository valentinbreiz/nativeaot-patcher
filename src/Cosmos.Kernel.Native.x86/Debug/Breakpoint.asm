global _native_debug_breakpoint

section .text

; void breakpoint()
; Triggers a software breakpoint (INT3) that GDB can catch
_native_debug_breakpoint:
    int3        ; Software breakpoint instruction
    ret