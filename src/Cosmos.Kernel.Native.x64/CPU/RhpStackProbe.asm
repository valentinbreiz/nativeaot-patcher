;; Stack probe helper
;; Probes each page from rsp down to r11 to ensure the guard page is touched
;; Input:
;;   r11 - lowest address of the allocated stack frame ([InitialSp - FrameSize])
;;   rsp - some byte on the last probed page
;; Output:
;;   rax - scratch (not preserved)
;;   r11 - preserved
;; Notes:
;;   Probes at least one page below rsp.
global RhpStackProbe
section .text


PROBE_STEP equ 0x1000

RhpStackProbe:
    ; Align rax to the start of the current page
    mov     rax, rsp
    and     rax, -PROBE_STEP        ; rax = lowest address on the last probed page

ProbeLoop:
    sub     rax, PROBE_STEP         ; move to the next page to probe
    test    dword [rax], eax        ; touch the page
    cmp     rax, r11
    jg      ProbeLoop               ; continue if still above r11

    ret
