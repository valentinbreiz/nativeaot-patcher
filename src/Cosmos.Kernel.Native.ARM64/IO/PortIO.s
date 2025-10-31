.global _native_io_read_byte
.global _native_io_write_byte

.text
.align 4

// ARM64 doesn't have port I/O like x86, so we provide stub implementations
// Actual hardware access on ARM64 should use memory-mapped I/O directly

// byte _native_io_read_byte(uint16_t port)
// w0 = port number
_native_io_read_byte:
    // ARM64 systems use MMIO instead of port I/O
    // Return 0 as stub - real drivers should use MMIO directly
    mov w0, #0
    ret

// void _native_io_write_byte(uint16_t port, uint8_t value)
// w0 = port number, w1 = value
_native_io_write_byte:
    // ARM64 systems use MMIO instead of port I/O
    // No-op as stub - real drivers should use MMIO directly
    ret
