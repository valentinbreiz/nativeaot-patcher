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
    lea     rsi, [rel __cosmos_exinfo_stack_head]

.pop_exinfo_loop:
    mov     rdi, [rsi]              ; current ExInfo
    test    rdi, rdi
    jz      .pop_exinfo_done        ; null = done
    cmp     rdi, r9
    jge     .pop_exinfo_done        ; >= resume SP = done
    mov     rdi, [rdi + OFFSETOF__ExInfo__m_pPrevExInfo]
    mov     [rsi], rdi              ; pop it
    jmp     .pop_exinfo_loop

.pop_exinfo_done:
    ; Reset SP to resume point
    ; NOTE: We're not popping our saved registers because we're never returning
    ; The funclet has already restored callee-saved registers for the target method
    mov     rsp, r9

    ; Jump to resume address (in r8)
    jmp     r8


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
