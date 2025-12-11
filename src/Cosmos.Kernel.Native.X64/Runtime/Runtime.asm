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
