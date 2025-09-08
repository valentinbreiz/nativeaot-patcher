.global _native_io_read_byte
.global _native_io_write_byte

.text
.align 4

# ARM64 doesn't have port I/O like x86, so we simulate it
# For now, these are stub implementations that could be mapped
# to specific memory addresses if needed

# byte _native_io_read_byte(uint16_t port)
# w0 = port number
_native_io_read_byte:
    # For ARM64 systems, port I/O is typically mapped to MMIO
    # This is a stub - in a real implementation, you'd map ports
    # to specific MMIO addresses based on the system
    mov w0, #0              // Return 0 for now
    ret

# void _native_io_write_byte(uint16_t port, uint8_t value)  
# w0 = port number, w1 = value
_native_io_write_byte:
    # This is a stub - in a real implementation, you'd map ports
    # to specific MMIO addresses based on the system
    ret
