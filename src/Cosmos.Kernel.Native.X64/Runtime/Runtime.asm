; x64 NativeAOT Runtime Stubs
; Security cookie, knob values accessor, and EH section accessors

global __security_cookie
global RhGetKnobValues
global get_eh_frame_start
global get_eh_frame_end
global get_dotnet_eh_table_start
global get_dotnet_eh_table_end

extern g_compilerEmbeddedKnobsBlob
extern __eh_frame_start
extern __eh_frame_end
extern __dotnet_eh_table_start
extern __dotnet_eh_table_end

section .data
align 8
__security_cookie:
    dq 0x2B992DDFA23249D6

section .text

; uint32_t RhGetKnobValues(char*** pResultKeys, char*** pResultValues)
;
; Retrieves compiler-embedded knob values for AppContext initialization
;
; Parameters:
;   rcx - pointer to receive keys array
;   rdx - pointer to receive values array
;
; Returns:
;   eax - count of knob entries
RhGetKnobValues:
    ; g_compilerEmbeddedKnobsBlob layout:
    ;   offset 0: m_count (uint32_t)
    ;   offset 8: m_first[] (flexible array of pointers)

    lea rax, [rel g_compilerEmbeddedKnobsBlob]

    ; Get count
    mov r8d, dword [rax]            ; r8d = m_count

    ; Calculate keys pointer (m_first starts at offset 8)
    lea r9, [rax + 8]               ; r9 = &m_first[0] (keys)
    mov qword [rcx], r9             ; *pResultKeys = keys

    ; Calculate values pointer (m_first + count * 8)
    mov r10d, r8d
    shl r10, 3                      ; r10 = count * 8
    lea r9, [rax + 8 + r10]         ; r9 = &m_first[count] (values)
    mov qword [rdx], r9             ; *pResultValues = values

    mov eax, r8d                    ; return count
    ret

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
