.global _native_debug_breakpoint
.global _native_debug_breakpoint_soft

.text
.align 4

// void breakpoint()
// Triggers a software breakpoint that a debugger can catch
_native_debug_breakpoint:
    brk #0              // ARM64 software breakpoint instruction
    ret

// void breakpoint_soft()
// A softer breakpoint that can be stepped over
_native_debug_breakpoint_soft:
    // If debugger is attached, it will stop here naturally
    nop                 // Debugger breakpoint location
    nop
    nop
    ret
