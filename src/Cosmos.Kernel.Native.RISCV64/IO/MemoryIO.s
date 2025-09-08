.global _native_mmio_read_byte
.global _native_mmio_write_byte

.text
.align 4

# Memory-mapped I/O operations for RISC-V
# a0 = address, a1 = value for writes

_native_mmio_read_byte:
    fence               # Memory fence before read
    lb      a0, 0(a0)   # Load byte
    fence               # Memory fence after read
    ret

_native_mmio_write_byte:
    fence               # Memory fence before write
    sb      a1, 0(a0)   # Store byte
    fence               # Memory fence after write
    ret