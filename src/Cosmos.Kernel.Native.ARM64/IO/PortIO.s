.global _native_io_read_byte
.global _native_io_read_word
.global _native_io_read_dword
.global _native_io_write_byte
.global _native_io_write_word
.global _native_io_write_dword

.text
.align 4

// ARM64 doesn't have port I/O like x86, so we provide stub implementations
// Actual hardware access on ARM64 should use memory-mapped I/O directly

// byte _native_io_read_byte(uint16_t port)
// w0 = port number
_native_io_read_byte:
    mov w0, #0
    ret

// ushort _native_io_read_word(uint16_t port)
// w0 = port number
_native_io_read_word:
    mov w0, #0
    ret

// uint _native_io_read_dword(uint16_t port)
// w0 = port number
_native_io_read_dword:
    mov w0, #0
    ret

// void _native_io_write_byte(uint16_t port, uint8_t value)
// w0 = port number, w1 = value
_native_io_write_byte:
    ret

// void _native_io_write_word(uint16_t port, uint16_t value)
// w0 = port number, w1 = value
_native_io_write_word:
    ret

// void _native_io_write_dword(uint16_t port, uint32_t value)
// w0 = port number, w1 = value
_native_io_write_dword:
    ret
