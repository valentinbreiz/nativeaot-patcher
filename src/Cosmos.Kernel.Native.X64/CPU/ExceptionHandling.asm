; Exception Handling Assembly Stubs for x86-64 (System V ABI / Linux)
; Implements the low-level exception dispatching for NativeAOT

section .data

; Global exception info stack head (single-threaded kernel, no TLS needed)
global __cosmos_exinfo_stack_head
__cosmos_exinfo_stack_head: dq 0

section .text

; External managed functions
extern RhThrowEx                    ; C# exception dispatcher

;=============================================================================
; Structure offsets (from AsmOffsetsCpu.h)
;=============================================================================

; ExInfo offsets
%define SIZEOF__ExInfo                  0x190
%define OFFSETOF__ExInfo__m_pPrevExInfo 0x00
%define OFFSETOF__ExInfo__m_pExContext  0x08
%define OFFSETOF__ExInfo__m_exception   0x10
%define OFFSETOF__ExInfo__m_kind        0x18
%define OFFSETOF__ExInfo__m_passNumber  0x19
%define OFFSETOF__ExInfo__m_idxCurClause 0x1c

; REGDISPLAY offsets
%define OFFSETOF__REGDISPLAY__SP        0x78
%define OFFSETOF__REGDISPLAY__pRbx      0x18
%define OFFSETOF__REGDISPLAY__pRbp      0x20
%define OFFSETOF__REGDISPLAY__pRsi      0x28
%define OFFSETOF__REGDISPLAY__pRdi      0x30
%define OFFSETOF__REGDISPLAY__pR12      0x58
%define OFFSETOF__REGDISPLAY__pR13      0x60
%define OFFSETOF__REGDISPLAY__pR14      0x68
%define OFFSETOF__REGDISPLAY__pR15      0x70

; ExKind enum
%define ExKind_Throw        1

; Stack size for ExInfo (aligned to 16 bytes)
%define STACKSIZEOF_ExInfo  ((SIZEOF__ExInfo + 15) & ~15)

;=============================================================================
; RhpThrowEx - Entry point for throwing a managed exception
;
; INPUT:  RDI = exception object
;
; This is called by the ILC-generated code when a 'throw' statement executes.
; System V AMD64 ABI: RDI = first argument (exception object)
;=============================================================================
global RhpThrowEx
RhpThrowEx:
    ; Save the RSP of the throw site (before call pushed return address)
    lea     rax, [rsp + 8]          ; rax = original RSP at throw site
    mov     rsi, [rsp]              ; rsi = return address (throw site IP)

    ; Align stack to 16 bytes
    xor     rdx, rdx
    push    rdx                     ; padding for alignment

    ; Build PAL_LIMITED_CONTEXT structure on stack
    ; Push in reverse order so they end up at correct offsets
    push    r15                     ; +0x48: R15
    push    r14                     ; +0x40: R14
    push    r13                     ; +0x38: R13
    push    r12                     ; +0x30: R12
    push    rdx                     ; +0x28: Rdx (0)
    push    rbx                     ; +0x20: Rbx
    push    rdx                     ; +0x18: Rax (0)
    push    rbp                     ; +0x10: Rbp
    push    rax                     ; +0x08: Rsp (original)
    push    rsi                     ; +0x00: IP (return address)

    ; Now RSP points to PAL_LIMITED_CONTEXT
    ; Allocate space for ExInfo
    sub     rsp, STACKSIZEOF_ExInfo

    ; Save exception object temporarily
    mov     rbx, rdi                ; rbx = exception object

    ; RSI = ExInfo* (current RSP)
    mov     rsi, rsp

    ; Initialize ExInfo fields
    xor     rdx, rdx
    mov     [rsi + OFFSETOF__ExInfo__m_exception], rdx           ; exception = null (set by managed code)
    mov     byte [rsi + OFFSETOF__ExInfo__m_passNumber], 1       ; passNumber = 1
    mov     dword [rsi + OFFSETOF__ExInfo__m_idxCurClause], 0xFFFFFFFF  ; idxCurClause = -1
    mov     byte [rsi + OFFSETOF__ExInfo__m_kind], ExKind_Throw  ; kind = Throw

    ; Link ExInfo into the global exception chain
    ; (In a real OS, this would be thread-local via INLINE_GETTHREAD)
    lea     rax, [rel __cosmos_exinfo_stack_head]
    mov     rdx, [rax]                                           ; rdx = current head
    mov     [rsi + OFFSETOF__ExInfo__m_pPrevExInfo], rdx         ; pExInfo->m_pPrevExInfo = head
    mov     [rax], rsi                                           ; head = pExInfo

    ; Set the exception context pointer
    lea     rdx, [rsp + STACKSIZEOF_ExInfo]                      ; rdx = PAL_LIMITED_CONTEXT*
    mov     [rsi + OFFSETOF__ExInfo__m_pExContext], rdx

    ; Call managed exception handler
    ; RDI = exception object (restore from rbx)
    ; RSI = ExInfo* (already set)
    mov     rdi, rbx
    call    RhThrowEx

    ; If we return, something went wrong (should never happen)
    ; RhThrowEx should either transfer to a handler or halt
    int     3
    jmp     $

;=============================================================================
; RhpCallCatchFunclet - Call a catch handler funclet
;
; INPUT:  RDI = exception object
;         RSI = handler funclet address
;         RDX = REGDISPLAY*
;         RCX = ExInfo*
;
; OUTPUT: RAX = resume address (where to continue after catch)
;
; The catch funclet expects the exception object in RDI.
; After the funclet returns, we resume at the address it returns.
;=============================================================================
global RhpCallCatchFunclet
RhpCallCatchFunclet:
    ; Save callee-saved registers
    push    rbp
    push    rbx
    push    r12
    push    r13
    push    r14
    push    r15

    ; Save arguments
    push    rdi                     ; exception object
    push    rsi                     ; handler address
    push    rdx                     ; REGDISPLAY*
    push    rcx                     ; ExInfo*

    ; Restore callee-saved registers from REGDISPLAY
    ; These are the registers the funclet expects to have the values from the throwing method
    mov     rax, [rdx + OFFSETOF__REGDISPLAY__pRbx]
    mov     rbx, [rax]
    mov     rax, [rdx + OFFSETOF__REGDISPLAY__pRbp]
    mov     rbp, [rax]
    mov     rax, [rdx + OFFSETOF__REGDISPLAY__pRsi]
    test    rax, rax
    jz      .skip_rsi
    mov     rsi, [rax]
.skip_rsi:
    mov     rax, [rdx + OFFSETOF__REGDISPLAY__pRdi]
    test    rax, rax
    jz      .skip_rdi
    mov     rdi, [rax]
.skip_rdi:
    mov     rax, [rdx + OFFSETOF__REGDISPLAY__pR12]
    mov     r12, [rax]
    mov     rax, [rdx + OFFSETOF__REGDISPLAY__pR13]
    mov     r13, [rax]
    mov     rax, [rdx + OFFSETOF__REGDISPLAY__pR14]
    mov     r14, [rax]
    mov     rax, [rdx + OFFSETOF__REGDISPLAY__pR15]
    mov     r15, [rax]

    ; Load exception object and call handler
    mov     rdi, [rsp + 24]         ; exception object
    call    qword [rsp + 16]        ; call handler funclet

    ; RAX now contains the resume address
    ; Save resume address to r8 (callee-saved r12-r15 are still valid from REGDISPLAY restore)
    mov     r8, rax

    ; Reload REGDISPLAY* from stack (rdx may have been clobbered by funclet)
    ; Stack layout: [ExInfo*][REGDISPLAY*][handler addr][exception obj]
    mov     rdx, [rsp + 8]          ; REGDISPLAY*

    ; Get resume SP from REGDISPLAY
    mov     r9, [rdx + OFFSETOF__REGDISPLAY__SP]   ; r9 = resume SP

    ; Pop ExInfo entries that are below the resume SP
    ; Use r10/r11 as scratch registers to avoid clobbering rdi/rsi which may be
    ; expected by the resume code
    lea     r11, [rel __cosmos_exinfo_stack_head]

.pop_exinfo_loop:
    mov     r10, [r11]              ; current ExInfo
    test    r10, r10
    jz      .pop_exinfo_done        ; null = done
    cmp     r10, r9
    jge     .pop_exinfo_done        ; >= resume SP = done
    mov     r10, [r10 + OFFSETOF__ExInfo__m_pPrevExInfo]
    mov     [r11], r10              ; pop it
    jmp     .pop_exinfo_loop

.pop_exinfo_done:
    ; Resume execution after the catch block
    ; The funclet has executed the catch block body, and r8 contains the resume address
    ; r9 contains the handler frame's RBP (REGDISPLAY.SP)

    ; Instead of jumping to the resume address (which has complex stack expectations),
    ; we'll directly return from the catching function (Run) to its caller (Start).

    ; From stack dump analysis:
    ;   [RBP + 16] = 'this' pointer (0xFFFF80000010D004)
    ;   [RBP + 8]  = return address to caller
    ;   [RBP + 0]  = caller's saved RBP
    ;   [RBP - 8]  = stack pointer value (not saved RBX)
    ;   [RBP - 16] = some data address
    ;   [RBP - 40] = 'this' pointer again
    ;   [RBP - 64] = 'this' pointer again

    ; Start() likely keeps 'this' in RBX across the call to Run()
    ; Restore RBX from [RBP+16] which appears to be 'this'
    mov     rbx, [r9 + 16]          ; Restore 'this' pointer to RBX

    ; Zero other callee-saved registers (safer than reading garbage)
    xor     r12, r12
    xor     r13, r13
    xor     r14, r14
    xor     r15, r15

    ; Set up stack to return from Run to Start
    ; r9 = Run's RBP
    ; [r9 + 0] = Start's saved RBP
    ; [r9 + 8] = return address in Start
    mov     rbp, [r9]               ; Restore Start's RBP
    mov     rsp, r9
    add     rsp, 8                  ; Point RSP to return address
    ret                             ; Return to Start (pops [r9+8] as return addr)


;=============================================================================
; RhpCallFilterFunclet - Call a filter funclet to evaluate exception filter
;
; INPUT:  RDI = exception object
;         RSI = filter funclet address
;         RDX = REGDISPLAY*
;
; OUTPUT: RAX = 1 if filter matched (should catch), 0 if not
;
; The filter funclet expects the exception object in RDI.
; It returns non-zero if the exception should be caught by this handler.
;=============================================================================
global RhpCallFilterFunclet
RhpCallFilterFunclet:
    ; Save callee-saved registers
    push    rbp
    push    rbx
    push    r12
    push    r13
    push    r14
    push    r15

    ; Save arguments
    push    rdi                     ; exception object
    push    rsi                     ; filter address
    push    rdx                     ; REGDISPLAY*

    ; Restore callee-saved registers from REGDISPLAY
    ; These are the registers the funclet expects to have the values from the method
    mov     rax, [rdx + OFFSETOF__REGDISPLAY__pRbx]
    test    rax, rax
    jz      .skip_rbx_f
    mov     rbx, [rax]
.skip_rbx_f:
    mov     rax, [rdx + OFFSETOF__REGDISPLAY__pRbp]
    test    rax, rax
    jz      .skip_rbp_f
    mov     rbp, [rax]
.skip_rbp_f:
    mov     rax, [rdx + OFFSETOF__REGDISPLAY__pR12]
    test    rax, rax
    jz      .skip_r12_f
    mov     r12, [rax]
.skip_r12_f:
    mov     rax, [rdx + OFFSETOF__REGDISPLAY__pR13]
    test    rax, rax
    jz      .skip_r13_f
    mov     r13, [rax]
.skip_r13_f:
    mov     rax, [rdx + OFFSETOF__REGDISPLAY__pR14]
    test    rax, rax
    jz      .skip_r14_f
    mov     r14, [rax]
.skip_r14_f:
    mov     rax, [rdx + OFFSETOF__REGDISPLAY__pR15]
    test    rax, rax
    jz      .skip_r15_f
    mov     r15, [rax]
.skip_r15_f:

    ; Load exception object and call filter
    mov     rdi, [rsp + 16]         ; exception object
    call    qword [rsp + 8]         ; call filter funclet

    ; RAX now contains the filter result (0 = no match, non-zero = match)
    mov     r8, rax                 ; save result

    ; Clean up stack and restore our callee-saved registers
    add     rsp, 24                 ; pop saved args
    pop     r15
    pop     r14
    pop     r13
    pop     r12
    pop     rbx
    pop     rbp

    ; Return filter result
    mov     rax, r8
    ret


;=============================================================================
; RhpRethrow - Rethrow the current exception
;
; Called when a 'throw;' statement (without exception object) is executed.
;=============================================================================
global RhpRethrow
RhpRethrow:
    ; Get current exception from ExInfo chain
    lea     rax, [rel __cosmos_exinfo_stack_head]
    mov     rax, [rax]
    test    rax, rax
    jz      .halt

    ; Get exception object from ExInfo
    mov     rdi, [rax + OFFSETOF__ExInfo__m_exception]
    test    rdi, rdi
    jz      .halt

    ; Rethrow using normal throw path
    jmp     RhpThrowEx

.halt:
    int     3
    jmp     $
