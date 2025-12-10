// SIMD-optimized memory copy operations for ARM64
// Uses NEON (Q/V registers) for 128-bit transfers

.global _simd_copy_16
.global _simd_copy_32
.global _simd_copy_64
.global _simd_copy_128
.global _simd_copy_128_blocks
.global _simd_fill_16_blocks

.text
.align 4

// void _simd_copy_16(void* dest, void* src)
// Copies 16 bytes using 1 NEON register
// ARM64: x0 = dest, x1 = src
_simd_copy_16:
    ldr     q0, [x1]            // Load 16 bytes from src into Q0
    str     q0, [x0]            // Store 16 bytes from Q0 to dest
    ret

// void _simd_copy_32(void* dest, void* src)
// Copies 32 bytes using 2 NEON registers
_simd_copy_32:
    ldp     q0, q1, [x1]        // Load 32 bytes (2x16) from src
    stp     q0, q1, [x0]        // Store 32 bytes to dest
    ret

// void _simd_copy_64(void* dest, void* src)
// Copies 64 bytes using 4 NEON registers
_simd_copy_64:
    ldp     q0, q1, [x1]        // Load first 32 bytes
    ldp     q2, q3, [x1, #32]   // Load next 32 bytes
    stp     q0, q1, [x0]        // Store first 32 bytes
    stp     q2, q3, [x0, #32]   // Store next 32 bytes
    ret

// void _simd_copy_128(void* dest, void* src)
// Copies 128 bytes using 8 NEON registers
_simd_copy_128:
    // Load all 128 bytes into Q0-Q7
    ldp     q0, q1, [x1]
    ldp     q2, q3, [x1, #32]
    ldp     q4, q5, [x1, #64]
    ldp     q6, q7, [x1, #96]
    // Store all 128 bytes from Q0-Q7
    stp     q0, q1, [x0]
    stp     q2, q3, [x0, #32]
    stp     q4, q5, [x0, #64]
    stp     q6, q7, [x0, #96]
    ret

// void _simd_copy_128_blocks(void* dest, void* src, int blockCount)
// Copies multiple 128-byte blocks
// ARM64: x0 = dest, x1 = src, x2 = blockCount
_simd_copy_128_blocks:
    // Check if blockCount is 0
    cbz     x2, .Lcopy_done

.Lcopy_loop:
    // Load 128 bytes into Q0-Q7
    ldp     q0, q1, [x1]
    ldp     q2, q3, [x1, #32]
    ldp     q4, q5, [x1, #64]
    ldp     q6, q7, [x1, #96]

    // Store 128 bytes from Q0-Q7
    stp     q0, q1, [x0]
    stp     q2, q3, [x0, #32]
    stp     q4, q5, [x0, #64]
    stp     q6, q7, [x0, #96]

    // Advance pointers by 128 bytes
    add     x1, x1, #128
    add     x0, x0, #128

    // Decrement block counter and loop if not zero
    subs    x2, x2, #1
    b.ne    .Lcopy_loop

.Lcopy_done:
    ret

// void _simd_fill_16_blocks(void* dest, int value, int blockCount)
// Fills memory with a 32-bit value in 16-byte blocks using SIMD
// ARM64: x0 = dest, w1 = value (32-bit), x2 = blockCount
_simd_fill_16_blocks:
    // Check if blockCount is 0
    cbz     x2, .Lfill_done

    // Broadcast the 32-bit value to all 4 dwords in Q0
    dup     v0.4s, w1           // Duplicate w1 to all 4 32-bit lanes of V0

.Lfill_loop:
    // Store 16 bytes
    str     q0, [x0], #16       // Store and post-increment

    // Decrement counter
    subs    x2, x2, #1
    b.ne    .Lfill_loop

.Lfill_done:
    ret
