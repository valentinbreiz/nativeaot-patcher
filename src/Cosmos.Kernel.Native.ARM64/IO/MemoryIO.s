.global _native_arm64_mmio_read_byte
.global _native_arm64_mmio_read_word
.global _native_arm64_mmio_read_dword
.global _native_arm64_mmio_write_byte
.global _native_arm64_mmio_write_word
.global _native_arm64_mmio_write_dword

.text
.align 4

// Memory-mapped I/O operations for ARM64
// x0 = address, w1/w1/x1 = value for writes

_native_arm64_mmio_read_byte:
    ldrb    w0, [x0]
    dmb     sy
    ret

_native_arm64_mmio_read_word:
    ldrh    w0, [x0]
    dmb     sy
    ret

_native_arm64_mmio_read_dword:
    ldr     w0, [x0]
    dmb     sy
    ret

_native_arm64_mmio_write_byte:
    dmb     sy
    strb    w1, [x0]
    dmb     sy
    ret

_native_arm64_mmio_write_word:
    dmb     sy
    strh    w1, [x0]
    dmb     sy
    ret

_native_arm64_mmio_write_dword:
    dmb     sy
    str     w1, [x0]
    dmb     sy
    ret
