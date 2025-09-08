.global _native_mmio_read_byte
.global _native_mmio_write_byte

.text
.align 4

# Memory-mapped I/O operations for ARM64
# x0 = address, w1 = value for writes

_native_mmio_read_byte:
    ldrb    w0, [x0]
    dmb     sy              # Memory barrier
    ret

_native_mmio_write_byte:
    dmb     sy              # Memory barrier before write
    strb    w1, [x0]
    dmb     sy              # Memory barrier after write
    ret