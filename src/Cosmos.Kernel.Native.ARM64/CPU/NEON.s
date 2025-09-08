.global EnableSSE

.text
.align 4

# ARM64 equivalent of EnableSSE - enable NEON/Advanced SIMD
# On ARM64, NEON is enabled by default, so this is essentially a no-op
EnableSSE:
    # NEON is always available on ARM64, no special initialization needed
    ret