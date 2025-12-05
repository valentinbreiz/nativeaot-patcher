// Exception Handling Assembly Stubs for ARM64 (AAPCS64)
// Implements the low-level exception dispatching for NativeAOT

.section .data

// Global exception info stack head (single-threaded kernel, no TLS needed)
.global __cosmos_exinfo_stack_head
__cosmos_exinfo_stack_head: .quad 0

.section .text

// External managed functions
.extern RhThrowEx                    // C# exception dispatcher

//=============================================================================
// Structure offsets
//=============================================================================

// ExInfo offsets
.equ SIZEOF__ExInfo,                  0x190
.equ OFFSETOF__ExInfo__m_pPrevExInfo, 0x00
.equ OFFSETOF__ExInfo__m_pExContext,  0x08
.equ OFFSETOF__ExInfo__m_exception,   0x10
.equ OFFSETOF__ExInfo__m_kind,        0x18
.equ OFFSETOF__ExInfo__m_passNumber,  0x19
.equ OFFSETOF__ExInfo__m_idxCurClause, 0x1c

// PAL_LIMITED_CONTEXT offsets for ARM64
.equ SIZEOF__PAL_LIMITED_CONTEXT,     0x50
.equ OFFSETOF__PAL_LIMITED_CONTEXT__SP,  0x00
.equ OFFSETOF__PAL_LIMITED_CONTEXT__IP,  0x08
.equ OFFSETOF__PAL_LIMITED_CONTEXT__FP,  0x10
.equ OFFSETOF__PAL_LIMITED_CONTEXT__LR,  0x18
.equ OFFSETOF__PAL_LIMITED_CONTEXT__X19, 0x20
.equ OFFSETOF__PAL_LIMITED_CONTEXT__X20, 0x28
.equ OFFSETOF__PAL_LIMITED_CONTEXT__X21, 0x30
.equ OFFSETOF__PAL_LIMITED_CONTEXT__X22, 0x38
.equ OFFSETOF__PAL_LIMITED_CONTEXT__X23, 0x40
.equ OFFSETOF__PAL_LIMITED_CONTEXT__X24, 0x48

// REGDISPLAY offsets for ARM64
.equ OFFSETOF__REGDISPLAY__SP,        0x00
.equ OFFSETOF__REGDISPLAY__pFP,       0x08
.equ OFFSETOF__REGDISPLAY__pX19,      0x10
.equ OFFSETOF__REGDISPLAY__pX20,      0x18
.equ OFFSETOF__REGDISPLAY__pX21,      0x20
.equ OFFSETOF__REGDISPLAY__pX22,      0x28
.equ OFFSETOF__REGDISPLAY__pX23,      0x30
.equ OFFSETOF__REGDISPLAY__pX24,      0x38
.equ OFFSETOF__REGDISPLAY__pX25,      0x40
.equ OFFSETOF__REGDISPLAY__pX26,      0x48
.equ OFFSETOF__REGDISPLAY__pX27,      0x50
.equ OFFSETOF__REGDISPLAY__pX28,      0x58

// ExKind enum
.equ ExKind_Throw,        1

// Stack size for ExInfo (aligned to 16 bytes)
.equ STACKSIZEOF_ExInfo,  ((SIZEOF__ExInfo + 15) & ~15)

//=============================================================================
// RhpThrowEx - Entry point for throwing a managed exception
//
// INPUT:  x0 = exception object
//
// This is called by the ILC-generated code when a 'throw' statement executes.
// AAPCS64: x0-x7 = arguments, x19-x28 = callee-saved
//=============================================================================
.global RhpThrowEx
.balign 4
RhpThrowEx:
    // Save the SP of the throw site and LR (return address)
    mov     x9, sp                      // x9 = original SP at throw site
    mov     x10, lr                     // x10 = return address (throw site IP)

    // Allocate space for PAL_LIMITED_CONTEXT (0x50 bytes, 16-byte aligned)
    sub     sp, sp, #SIZEOF__PAL_LIMITED_CONTEXT

    // Build PAL_LIMITED_CONTEXT structure on stack
    str     x9, [sp, #OFFSETOF__PAL_LIMITED_CONTEXT__SP]   // SP
    str     x10, [sp, #OFFSETOF__PAL_LIMITED_CONTEXT__IP]  // IP (return address)
    str     x29, [sp, #OFFSETOF__PAL_LIMITED_CONTEXT__FP]  // FP
    str     x30, [sp, #OFFSETOF__PAL_LIMITED_CONTEXT__LR]  // LR
    str     x19, [sp, #OFFSETOF__PAL_LIMITED_CONTEXT__X19]
    str     x20, [sp, #OFFSETOF__PAL_LIMITED_CONTEXT__X20]
    str     x21, [sp, #OFFSETOF__PAL_LIMITED_CONTEXT__X21]
    str     x22, [sp, #OFFSETOF__PAL_LIMITED_CONTEXT__X22]
    str     x23, [sp, #OFFSETOF__PAL_LIMITED_CONTEXT__X23]
    str     x24, [sp, #OFFSETOF__PAL_LIMITED_CONTEXT__X24]

    // Save exception object in callee-saved register
    mov     x19, x0                     // x19 = exception object

    // Allocate space for ExInfo
    sub     sp, sp, #STACKSIZEOF_ExInfo

    // x1 = ExInfo* (current SP)
    mov     x1, sp

    // Initialize ExInfo fields
    str     xzr, [x1, #OFFSETOF__ExInfo__m_exception]      // exception = null
    mov     w2, #1
    strb    w2, [x1, #OFFSETOF__ExInfo__m_passNumber]      // passNumber = 1
    mov     w2, #0xFFFFFFFF
    str     w2, [x1, #OFFSETOF__ExInfo__m_idxCurClause]    // idxCurClause = -1
    mov     w2, #ExKind_Throw
    strb    w2, [x1, #OFFSETOF__ExInfo__m_kind]            // kind = Throw

    // Link ExInfo into the global exception chain
    adrp    x2, __cosmos_exinfo_stack_head
    add     x2, x2, :lo12:__cosmos_exinfo_stack_head
    ldr     x3, [x2]                                        // x3 = current head
    str     x3, [x1, #OFFSETOF__ExInfo__m_pPrevExInfo]     // pExInfo->m_pPrevExInfo = head
    str     x1, [x2]                                        // head = pExInfo

    // Set the exception context pointer
    add     x3, sp, #STACKSIZEOF_ExInfo                    // x3 = PAL_LIMITED_CONTEXT*
    str     x3, [x1, #OFFSETOF__ExInfo__m_pExContext]

    // Call managed exception handler
    // x0 = exception object (restore from x19)
    // x1 = ExInfo* (already set)
    mov     x0, x19
    bl      RhThrowEx

    // If we return, something went wrong (should never happen)
    brk     #0
    b       .

//=============================================================================
// RhpCallCatchFunclet - Call a catch handler funclet
//
// INPUT:  x0 = exception object
//         x1 = handler funclet address
//         x2 = REGDISPLAY*
//         x3 = ExInfo*
//
// OUTPUT: x0 = resume address (where to continue after catch)
//
// The catch funclet expects the exception object in x0.
// After the funclet returns, we resume at the address it returns.
//=============================================================================
.global RhpCallCatchFunclet
.balign 4
RhpCallCatchFunclet:
    // Save callee-saved registers and arguments
    stp     x29, x30, [sp, #-0x60]!
    stp     x19, x20, [sp, #0x10]
    stp     x21, x22, [sp, #0x20]
    stp     x23, x24, [sp, #0x30]
    stp     x25, x26, [sp, #0x40]
    stp     x27, x28, [sp, #0x50]

    // Save arguments for later
    mov     x19, x0                     // x19 = exception object
    mov     x20, x1                     // x20 = handler address
    mov     x21, x2                     // x21 = REGDISPLAY*
    mov     x22, x3                     // x22 = ExInfo*

    // Restore callee-saved registers from REGDISPLAY
    ldr     x9, [x21, #OFFSETOF__REGDISPLAY__pFP]
    ldr     x29, [x9]
    ldr     x9, [x21, #OFFSETOF__REGDISPLAY__pX19]
    cbz     x9, 1f
    ldr     x23, [x9]                   // Use x23 temporarily, will restore x19 later
1:
    ldr     x9, [x21, #OFFSETOF__REGDISPLAY__pX20]
    cbz     x9, 2f
    ldr     x24, [x9]                   // Use x24 temporarily
2:
    ldr     x9, [x21, #OFFSETOF__REGDISPLAY__pX21]
    cbz     x9, 3f
    ldr     x25, [x9]                   // Use x25 temporarily
3:
    ldr     x9, [x21, #OFFSETOF__REGDISPLAY__pX22]
    cbz     x9, 4f
    ldr     x26, [x9]                   // Use x26 temporarily
4:
    ldr     x9, [x21, #OFFSETOF__REGDISPLAY__pX23]
    cbz     x9, 5f
    ldr     x27, [x9]                   // Use x27 temporarily
5:
    ldr     x9, [x21, #OFFSETOF__REGDISPLAY__pX24]
    cbz     x9, 6f
    ldr     x28, [x9]                   // Use x28 temporarily
6:

    // Call handler funclet
    // x0 = exception object
    mov     x0, x19
    blr     x20

    // x0 now contains the resume address
    mov     x10, x0                     // x10 = resume address

    // Get resume SP from REGDISPLAY
    ldr     x11, [x21, #OFFSETOF__REGDISPLAY__SP]   // x11 = resume SP

    // Pop ExInfo entries that are below the resume SP
    adrp    x8, __cosmos_exinfo_stack_head
    add     x8, x8, :lo12:__cosmos_exinfo_stack_head

.pop_exinfo_loop:
    ldr     x9, [x8]                    // current ExInfo
    cbz     x9, .pop_exinfo_done        // null = done
    cmp     x9, x11
    b.ge    .pop_exinfo_done            // >= resume SP = done
    ldr     x9, [x9, #OFFSETOF__ExInfo__m_pPrevExInfo]
    str     x9, [x8]                    // pop it
    b       .pop_exinfo_loop

.pop_exinfo_done:
    // Reset SP to resume point and jump
    mov     sp, x11
    br      x10


//=============================================================================
// RhpRethrow - Rethrow the current exception
//
// Called when a 'throw;' statement (without exception object) is executed.
//=============================================================================
.global RhpRethrow
.balign 4
RhpRethrow:
    // Get current exception from ExInfo chain
    adrp    x0, __cosmos_exinfo_stack_head
    add     x0, x0, :lo12:__cosmos_exinfo_stack_head
    ldr     x0, [x0]
    cbz     x0, .halt

    // Get exception object from ExInfo
    ldr     x0, [x0, #OFFSETOF__ExInfo__m_exception]
    cbz     x0, .halt

    // Rethrow using normal throw path
    b       RhpThrowEx

.halt:
    brk     #0
    b       .
