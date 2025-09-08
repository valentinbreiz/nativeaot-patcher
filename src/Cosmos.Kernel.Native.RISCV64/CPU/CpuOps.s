.global _native_cpu_halt
.global _native_cpu_memory_fence

.text
.align 4

_native_cpu_halt:
    wfi                     # Wait for interrupt
    ret

_native_cpu_memory_fence:
    fence                   # Memory fence
    ret