; x64 NativeAOT Runtime Stubs
; EH section accessors

global get_eh_frame_start
global get_eh_frame_end
global get_dotnet_eh_table_start
global get_dotnet_eh_table_end

extern __eh_frame_start
extern __eh_frame_end
extern __dotnet_eh_table_start
extern __dotnet_eh_table_end

global cos
global sin
global tan
global pow

section .text

; void* get_eh_frame_start(void)
; Returns pointer to start of .eh_frame section
get_eh_frame_start:
    lea rax, [rel __eh_frame_start]
    ret

; void* get_eh_frame_end(void)
; Returns pointer to end of .eh_frame section
get_eh_frame_end:
    lea rax, [rel __eh_frame_end]
    ret

; void* get_dotnet_eh_table_start(void)
; Returns pointer to start of .dotnet_eh_table section
get_dotnet_eh_table_start:
    lea rax, [rel __dotnet_eh_table_start]
    ret

; void* get_dotnet_eh_table_end(void)
; Returns pointer to end of .dotnet_eh_table section
get_dotnet_eh_table_end:
    lea rax, [rel __dotnet_eh_table_end]
    ret

; double cos(double x)
cos:
    sub rsp, 8
    movsd [rsp], xmm0
    fld qword [rsp]
    fcos
    fstp qword [rsp]
    movsd xmm0, [rsp]
    add rsp, 8
    ret

; double sin(double x)
sin:
    sub rsp, 8
    movsd [rsp], xmm0
    fld qword [rsp]
    fsin
    fstp qword [rsp]
    movsd xmm0, [rsp]
    add rsp, 8
    ret

; double tan(double x)

tan:
    sub rsp, 8
    movsd [rsp], xmm0
    fld qword [rsp]
    fptan
    fstp st0        ; fptan pushes 1.0, we pop it to leave just the result
    fstp qword [rsp]
    movsd xmm0, [rsp]
    add rsp, 8
    ret

; double pow(double x, double y)
pow:
    ; Stub: returns x for now (or 0)
    ; Real implementation requires complex log/exp logic
    ret
