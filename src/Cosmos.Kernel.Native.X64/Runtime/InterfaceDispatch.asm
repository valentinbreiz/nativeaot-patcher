; Licensed to the .NET Foundation under one or more agreements.
; The .NET Foundation licenses this file to you under the MIT license.
;
; Interface dispatch stubs for Cosmos OS (x64)

section .text

extern RhpCidResolve

; Initial dispatch on an interface when we don't have a cache yet.
; This is the entry point called from interface dispatch sites before
; the dispatch cell has been resolved.
;
; On entry (System V ABI):
;   rdi = 'this' pointer (the object we're dispatching on)
;   r11 = pointer to the interface dispatch cell
;
; The dispatch cell structure is:
;   Cell[0].m_pStub  = pointer to this function (RhpInitialDynamicInterfaceDispatch)
;   Cell[0].m_pCache = interface type pointer | flags
;   Cell[1].m_pStub  = 0
;   Cell[1].m_pCache = interface slot number
;
global RhpInitialDynamicInterfaceDispatch
align 16
RhpInitialDynamicInterfaceDispatch:
    ; Trigger an AV if we're dispatching on a null this
    cmp     byte [rdi], 0

    ; Save registers that will be clobbered
    ; Interface method parameters: rdi='this', rsi=param1, rdx=param2, etc.
    push    rdi        ; Save 'this' pointer
    push    rsi        ; Save first parameter
    push    rdx        ; Save second parameter

    ; Set up parameters for RhpCidResolve (System V ABI):
    ;   rdi = 'this' pointer (already in rdi, still there)
    ;   rsi = dispatch cell pointer
    mov     rsi, r11

    ; Call RhpCidResolve to resolve the interface method
    ; It returns the target method address in RAX
    call    RhpCidResolve

    ; Restore the original parameters for the interface method
    pop     rdx        ; Restore second parameter
    pop     rsi        ; Restore first parameter
    pop     rdi        ; Restore 'this' pointer

    ; Jump to the resolved method address (in RAX)
    jmp     rax
