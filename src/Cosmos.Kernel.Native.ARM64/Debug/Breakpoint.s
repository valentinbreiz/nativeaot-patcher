.global _native_debug_breakpoint_soft

.text
.align 4

# Software breakpoint for ARM64 debugging
_native_debug_breakpoint_soft:
    # ARM64 software breakpoint instruction
    # This triggers a debug exception that can be caught by a debugger
    brk #0
    ret