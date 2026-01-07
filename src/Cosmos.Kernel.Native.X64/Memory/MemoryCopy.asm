; SIMD-optimized memory copy operations for x64
; Uses SSE (XMM registers) for 128-bit transfers

global _simd_copy_16
global _simd_copy_32
global _simd_copy_64
global _simd_copy_128
global _simd_copy_128_blocks
global _simd_fill_16_blocks

section .text

; void _simd_copy_16(void* dest, void* src)
; Copies 16 bytes using 1 XMM register
; Windows x64: rcx = dest, rdx = src
; System V x64: rdi = dest, rsi = src
_simd_copy_16:
    ; Load 16 bytes from source into XMM0
    movdqu  xmm0, [rsi]
    ; Store 16 bytes from XMM0 to destination
    movdqu  [rdi], xmm0
    ret

; void _simd_copy_32(void* dest, void* src)
; Copies 32 bytes using 2 XMM registers
_simd_copy_32:
    movdqu  xmm0, [rsi]
    movdqu  xmm1, [rsi + 16]
    movdqu  [rdi], xmm0
    movdqu  [rdi + 16], xmm1
    ret

; void _simd_copy_64(void* dest, void* src)
; Copies 64 bytes using 4 XMM registers
_simd_copy_64:
    movdqu  xmm0, [rsi]
    movdqu  xmm1, [rsi + 16]
    movdqu  xmm2, [rsi + 32]
    movdqu  xmm3, [rsi + 48]
    movdqu  [rdi], xmm0
    movdqu  [rdi + 16], xmm1
    movdqu  [rdi + 32], xmm2
    movdqu  [rdi + 48], xmm3
    ret

; void _simd_copy_128(void* dest, void* src)
; Copies 128 bytes using all 8 XMM registers
_simd_copy_128:
    ; Load all 128 bytes into XMM0-XMM7
    movdqu  xmm0, [rsi]
    movdqu  xmm1, [rsi + 16]
    movdqu  xmm2, [rsi + 32]
    movdqu  xmm3, [rsi + 48]
    movdqu  xmm4, [rsi + 64]
    movdqu  xmm5, [rsi + 80]
    movdqu  xmm6, [rsi + 96]
    movdqu  xmm7, [rsi + 112]
    ; Store all 128 bytes from XMM0-XMM7
    movdqu  [rdi], xmm0
    movdqu  [rdi + 16], xmm1
    movdqu  [rdi + 32], xmm2
    movdqu  [rdi + 48], xmm3
    movdqu  [rdi + 64], xmm4
    movdqu  [rdi + 80], xmm5
    movdqu  [rdi + 96], xmm6
    movdqu  [rdi + 112], xmm7
    ret

; void _simd_copy_128_blocks(void* dest, void* src, int blockCount)
; Copies multiple 128-byte blocks
; System V x64: rdi = dest, rsi = src, rdx = blockCount
_simd_copy_128_blocks:
    ; Check if blockCount is 0
    test    rdx, rdx
    jz      .done

.loop:
    ; Load 128 bytes into XMM0-XMM7
    movdqu  xmm0, [rsi]
    movdqu  xmm1, [rsi + 16]
    movdqu  xmm2, [rsi + 32]
    movdqu  xmm3, [rsi + 48]
    movdqu  xmm4, [rsi + 64]
    movdqu  xmm5, [rsi + 80]
    movdqu  xmm6, [rsi + 96]
    movdqu  xmm7, [rsi + 112]

    ; Store 128 bytes from XMM0-XMM7
    movdqu  [rdi], xmm0
    movdqu  [rdi + 16], xmm1
    movdqu  [rdi + 32], xmm2
    movdqu  [rdi + 48], xmm3
    movdqu  [rdi + 64], xmm4
    movdqu  [rdi + 80], xmm5
    movdqu  [rdi + 96], xmm6
    movdqu  [rdi + 112], xmm7

    ; Advance pointers by 128 bytes
    add     rsi, 128
    add     rdi, 128

    ; Decrement block counter and loop if not zero
    dec     rdx
    jnz     .loop

.done:
    ret

; void _simd_fill_16_blocks(void* dest, int value, int blockCount)
; Fills memory with a 32-bit value in 16-byte blocks using SIMD
; System V x64: rdi = dest, rsi = value (32-bit), rdx = blockCount
_simd_fill_16_blocks:
    ; Check if blockCount is 0
    test    rdx, rdx
    jz      .fill_done

    ; Broadcast the 32-bit value to all 4 dwords in XMM0
    movd    xmm0, esi               ; Move 32-bit value to XMM0
    pshufd  xmm0, xmm0, 0           ; Broadcast to all 4 dwords

.fill_loop:
    ; Store 16 bytes
    movdqu  [rdi], xmm0

    ; Advance pointer
    add     rdi, 16

    ; Decrement counter
    dec     rdx
    jnz     .fill_loop

.fill_done:
    ret
