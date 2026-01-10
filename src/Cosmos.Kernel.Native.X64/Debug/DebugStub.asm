; ============================================================================
; Cosmos Debug Stub for x86_64
; Based on Gen2 Cosmos debug stub, adapted for NativeAOT
; ============================================================================

global _debug_stub_init
global _debug_stub_executing
global _debug_stub_break
global _debug_stub_send_trace
global _debug_stub_process_command
global _debug_stub_set_breakpoint
global _debug_stub_clear_breakpoint

; Debug status constants
%define Status_Run      0
%define Status_Break    1

; Step trigger constants
%define StepTrigger_None    0
%define StepTrigger_Into    1
%define StepTrigger_Over    2
%define StepTrigger_Out     3

; Protocol: Device to Host (Ds2Vs)
%define Ds2Vs_Noop          0
%define Ds2Vs_TracePoint    1
%define Ds2Vs_BreakPoint    3
%define Ds2Vs_Started       6
%define Ds2Vs_MethodContext 7
%define Ds2Vs_MemoryData    8
%define Ds2Vs_CmdCompleted  9
%define Ds2Vs_Registers     10
%define Ds2Vs_Frame         11
%define Ds2Vs_Stack         12

; Protocol: Host to Device (Vs2Ds)
%define Vs2Ds_Noop          0
%define Vs2Ds_Break         3
%define Vs2Ds_Continue      4
%define Vs2Ds_StepInto      5
%define Vs2Ds_BreakOnAddress 6
%define Vs2Ds_SendMethodContext 9
%define Vs2Ds_SendMemory    10
%define Vs2Ds_StepOver      11
%define Vs2Ds_StepOut       12
%define Vs2Ds_SendRegisters 13
%define Vs2Ds_SendFrame     14
%define Vs2Ds_SendStack     15
%define Vs2Ds_SetINT3       19
%define Vs2Ds_ClearINT3     20

; Serial port constants (COM1)
%define COM1_PORT       0x3F8
%define COM1_DATA       0x3F8
%define COM1_IER        0x3F9
%define COM1_FCR        0x3FA
%define COM1_LCR        0x3FB
%define COM1_MCR        0x3FC
%define COM1_LSR        0x3FD

section .data

; Debug state variables
align 8
DebugStatus:        dq Status_Run
DebugBreakOnNext:   dq StepTrigger_None
BreakRBP:           dq 0
CallerRIP:          dq 0
CallerRSP:          dq 0
CallerRBP:          dq 0
MaxBPId:            dq 0
CommandID:          dq 0
TraceMode:          dq 0

; Saved registers for inspection
align 8
SavedRAX:   dq 0
SavedRBX:   dq 0
SavedRCX:   dq 0
SavedRDX:   dq 0
SavedRSI:   dq 0
SavedRDI:   dq 0
SavedRBP:   dq 0
SavedRSP:   dq 0
SavedR8:    dq 0
SavedR9:    dq 0
SavedR10:   dq 0
SavedR11:   dq 0
SavedR12:   dq 0
SavedR13:   dq 0
SavedR14:   dq 0
SavedR15:   dq 0
SavedRIP:   dq 0
SavedRFLAGS: dq 0

; Breakpoint table (256 entries, each is an 8-byte address)
align 8
DebugBPs:   times 256 dq 0

; Serial signature for protocol sync
SerialSignature: dd 0x19740807

section .text

; ============================================================================
; Serial Communication Helpers
; ============================================================================

; Initialize COM1 for debug communication
; void debug_stub_init()
_debug_stub_init:
    push rbx

    ; Disable interrupts on COM1
    mov dx, COM1_IER
    xor al, al
    out dx, al

    ; Enable DLAB (set baud rate divisor)
    mov dx, COM1_LCR
    mov al, 0x80
    out dx, al

    ; Set divisor to 1 (115200 baud)
    mov dx, COM1_DATA
    mov al, 1
    out dx, al
    mov dx, COM1_IER
    xor al, al
    out dx, al

    ; 8 bits, no parity, one stop bit
    mov dx, COM1_LCR
    mov al, 0x03
    out dx, al

    ; Enable FIFO, clear them, with 14-byte threshold
    mov dx, COM1_FCR
    mov al, 0xC7
    out dx, al

    ; IRQs enabled, RTS/DSR set
    mov dx, COM1_MCR
    mov al, 0x0B
    out dx, al

    ; Send "Started" message
    mov al, Ds2Vs_Started
    call serial_write_byte

    pop rbx
    ret

; Write a byte to serial port
; Input: AL = byte to send
serial_write_byte:
    push rdx
    push rax

.wait_ready:
    mov dx, COM1_LSR
    in al, dx
    test al, 0x20           ; Check if transmit buffer empty
    jz .wait_ready

    pop rax
    mov dx, COM1_DATA
    out dx, al

    pop rdx
    ret

; Read a byte from serial port (blocking)
; Output: AL = byte read
serial_read_byte:
    push rdx

.wait_data:
    mov dx, COM1_LSR
    in al, dx
    test al, 0x01           ; Check if data available
    jz .wait_data

    mov dx, COM1_DATA
    in al, dx

    pop rdx
    ret

; Check if data is available on serial port
; Output: AL = 1 if data available, 0 otherwise
serial_data_available:
    push rdx
    mov dx, COM1_LSR
    in al, dx
    and al, 0x01
    pop rdx
    ret

; Write a 64-bit value to serial (little-endian)
; Input: RAX = value to send
serial_write_qword:
    push rcx
    push rax

    mov rcx, 8
.loop:
    call serial_write_byte
    shr rax, 8
    dec rcx
    jnz .loop

    pop rax
    pop rcx
    ret

; Read a 64-bit value from serial (little-endian)
; Output: RAX = value read
serial_read_qword:
    push rcx
    push rbx

    xor rax, rax
    xor rbx, rbx
    mov rcx, 8

.loop:
    push rcx
    call serial_read_byte
    pop rcx
    movzx rbx, al
    dec rcx
    shl rbx, cl
    shl rbx, cl
    shl rbx, cl
    shl rbx, cl
    shl rbx, cl
    shl rbx, cl
    shl rbx, cl
    shl rbx, cl
    or rax, rbx
    test rcx, rcx
    jnz .loop

    pop rbx
    pop rcx
    ret

; ============================================================================
; Debug Stub Core Functions
; ============================================================================

; Save all registers for inspection
save_all_registers:
    mov [rel SavedRAX], rax
    mov [rel SavedRBX], rbx
    mov [rel SavedRCX], rcx
    mov [rel SavedRDX], rdx
    mov [rel SavedRSI], rsi
    mov [rel SavedRDI], rdi
    mov [rel SavedRBP], rbp
    mov [rel SavedRSP], rsp
    mov [rel SavedR8], r8
    mov [rel SavedR9], r9
    mov [rel SavedR10], r10
    mov [rel SavedR11], r11
    mov [rel SavedR12], r12
    mov [rel SavedR13], r13
    mov [rel SavedR14], r14
    mov [rel SavedR15], r15

    ; Save flags
    pushfq
    pop rax
    mov [rel SavedRFLAGS], rax
    mov rax, [rel SavedRAX]
    ret

; Send trace point info (registers + RIP)
; void debug_stub_send_trace()
_debug_stub_send_trace:
    push rax
    push rbx
    push rcx

    ; Send trace message type
    mov al, Ds2Vs_TracePoint
    call serial_write_byte

    ; Send RIP
    mov rax, [rel SavedRIP]
    call serial_write_qword

    pop rcx
    pop rbx
    pop rax
    ret

; Send all registers to debugger
send_registers:
    push rax
    push rcx

    ; Send registers message type
    mov al, Ds2Vs_Registers
    call serial_write_byte

    ; Send all 16 general purpose registers + RIP + RFLAGS
    mov rax, [rel SavedRAX]
    call serial_write_qword
    mov rax, [rel SavedRBX]
    call serial_write_qword
    mov rax, [rel SavedRCX]
    call serial_write_qword
    mov rax, [rel SavedRDX]
    call serial_write_qword
    mov rax, [rel SavedRSI]
    call serial_write_qword
    mov rax, [rel SavedRDI]
    call serial_write_qword
    mov rax, [rel SavedRBP]
    call serial_write_qword
    mov rax, [rel SavedRSP]
    call serial_write_qword
    mov rax, [rel SavedR8]
    call serial_write_qword
    mov rax, [rel SavedR9]
    call serial_write_qword
    mov rax, [rel SavedR10]
    call serial_write_qword
    mov rax, [rel SavedR11]
    call serial_write_qword
    mov rax, [rel SavedR12]
    call serial_write_qword
    mov rax, [rel SavedR13]
    call serial_write_qword
    mov rax, [rel SavedR14]
    call serial_write_qword
    mov rax, [rel SavedR15]
    call serial_write_qword
    mov rax, [rel SavedRIP]
    call serial_write_qword
    mov rax, [rel SavedRFLAGS]
    call serial_write_qword

    pop rcx
    pop rax
    ret

; Send memory data to debugger
; Reads address and length from serial, sends data back
send_memory:
    push rax
    push rbx
    push rcx
    push rsi

    ; Read address
    call serial_read_qword
    mov rsi, rax

    ; Read length
    call serial_read_qword
    mov rcx, rax

    ; Send memory data message type
    mov al, Ds2Vs_MemoryData
    call serial_write_byte

    ; Send length
    mov rax, rcx
    call serial_write_qword

    ; Send data bytes
.send_loop:
    test rcx, rcx
    jz .done

    mov al, [rsi]
    call serial_write_byte
    inc rsi
    dec rcx
    jmp .send_loop

.done:
    pop rsi
    pop rcx
    pop rbx
    pop rax
    ret

; Send stack frame info
send_frame:
    push rax
    push rcx
    push rsi

    ; Send frame message type
    mov al, Ds2Vs_Frame
    call serial_write_byte

    ; Send RBP (frame pointer)
    mov rax, [rel SavedRBP]
    call serial_write_qword

    ; Send RSP
    mov rax, [rel SavedRSP]
    call serial_write_qword

    ; Send return address (at RBP+8)
    mov rsi, [rel SavedRBP]
    mov rax, [rsi + 8]
    call serial_write_qword

    pop rsi
    pop rcx
    pop rax
    ret

; Send acknowledgment
send_ack:
    push rax
    mov al, Ds2Vs_CmdCompleted
    call serial_write_byte
    pop rax
    ret

; ============================================================================
; Breakpoint Management
; ============================================================================

; Set a breakpoint at address
; void debug_stub_set_breakpoint(uint8_t id, uint64_t address)
; RDI = id, RSI = address
_debug_stub_set_breakpoint:
    push rax
    push rbx

    ; Calculate table offset
    lea rbx, [rel DebugBPs]
    movzx rax, dil          ; id
    shl rax, 3              ; * 8 (64-bit addresses)
    add rbx, rax

    ; Store address in table
    mov [rbx], rsi

    ; Write INT3 at target address
    mov byte [rsi], 0xCC

    ; Update MaxBPId if needed
    movzx rax, dil
    inc rax
    cmp rax, [rel MaxBPId]
    jbe .skip_update
    mov [rel MaxBPId], rax
.skip_update:

    pop rbx
    pop rax
    ret

; Clear a breakpoint
; void debug_stub_clear_breakpoint(uint8_t id)
; RDI = id
_debug_stub_clear_breakpoint:
    push rax
    push rbx
    push rsi

    ; Calculate table offset
    lea rbx, [rel DebugBPs]
    movzx rax, dil          ; id
    shl rax, 3              ; * 8
    add rbx, rax

    ; Get stored address
    mov rsi, [rbx]
    test rsi, rsi
    jz .done

    ; Write NOP at target address
    mov byte [rsi], 0x90

    ; Clear table entry
    mov qword [rbx], 0

    ; Rescan for MaxBPId
    call rescan_max_bp_id

.done:
    pop rsi
    pop rbx
    pop rax
    ret

; Rescan breakpoint table to find highest active BP ID
rescan_max_bp_id:
    push rax
    push rbx
    push rcx

    lea rbx, [rel DebugBPs]
    mov rcx, 255

.scan_loop:
    mov rax, [rbx + rcx * 8]
    test rax, rax
    jnz .found

    dec rcx
    jns .scan_loop

    ; No breakpoints found
    mov qword [rel MaxBPId], 0
    jmp .done

.found:
    inc rcx
    mov [rel MaxBPId], rcx

.done:
    pop rcx
    pop rbx
    pop rax
    ret

; ============================================================================
; Main Debug Execution Handler
; ============================================================================

; Called on each trace point / potential break
; void debug_stub_executing(uint64_t rip, uint64_t rbp)
; RDI = current RIP, RSI = current RBP
_debug_stub_executing:
    push rax
    push rbx
    push rcx
    push rdx

    ; Save caller info
    mov [rel CallerRIP], rdi
    mov [rel SavedRIP], rdi
    mov [rel CallerRBP], rsi

    call save_all_registers

    ; Check for breakpoint hit
    mov rcx, [rel MaxBPId]
    test rcx, rcx
    jz .skip_bp_scan

    lea rbx, [rel DebugBPs]
.bp_scan:
    dec rcx
    js .skip_bp_scan

    mov rax, [rbx + rcx * 8]
    cmp rax, rdi            ; Compare with current RIP
    jne .bp_scan

    ; Breakpoint hit!
    call do_break
    jmp .check_cmd

.skip_bp_scan:
    ; Check step triggers
    mov rax, [rel DebugBreakOnNext]

    ; Step Into - break on any trace
    cmp rax, StepTrigger_Into
    jne .check_step_over
    call do_break
    jmp .check_cmd

.check_step_over:
    cmp rax, StepTrigger_Over
    jne .check_step_out

    ; Break if RBP >= BreakRBP (same or caller frame)
    mov rax, [rel CallerRBP]
    cmp rax, [rel BreakRBP]
    jb .check_cmd
    call do_break
    jmp .check_cmd

.check_step_out:
    cmp rax, StepTrigger_Out
    jne .check_cmd

    ; Break if RBP > BreakRBP (caller frame only)
    mov rax, [rel CallerRBP]
    cmp rax, [rel BreakRBP]
    jbe .check_cmd
    call do_break

.check_cmd:
    ; Check for incoming commands (non-blocking)
    call serial_data_available
    test al, al
    jz .done

    call _debug_stub_process_command
    jmp .check_cmd

.done:
    pop rdx
    pop rcx
    pop rbx
    pop rax
    ret

; Enter break state and wait for commands
do_break:
    ; Reset step trigger
    mov qword [rel DebugBreakOnNext], StepTrigger_None
    mov qword [rel BreakRBP], 0

    ; Set break status
    mov qword [rel DebugStatus], Status_Break

    ; Send trace point
    call _debug_stub_send_trace

    ; Wait for and process commands
.wait_cmd:
    call _debug_stub_process_command

    ; Check if we should continue
    mov al, [rel CommandID]
    cmp al, Vs2Ds_Continue
    je .exit_break

    cmp al, Vs2Ds_StepInto
    je .step_into

    cmp al, Vs2Ds_StepOver
    je .step_over

    cmp al, Vs2Ds_StepOut
    je .step_out

    jmp .wait_cmd

.step_into:
    mov qword [rel DebugBreakOnNext], StepTrigger_Into
    jmp .exit_break

.step_over:
    mov qword [rel DebugBreakOnNext], StepTrigger_Over
    mov rax, [rel CallerRBP]
    mov [rel BreakRBP], rax
    jmp .exit_break

.step_out:
    mov qword [rel DebugBreakOnNext], StepTrigger_Out
    mov rax, [rel CallerRBP]
    mov [rel BreakRBP], rax

.exit_break:
    call send_ack
    mov qword [rel DebugStatus], Status_Run
    ret

; Process incoming debug command
; void debug_stub_process_command()
_debug_stub_process_command:
    push rax
    push rbx
    push rcx
    push rdx
    push rsi
    push rdi

    ; Read command byte
    call serial_read_byte
    mov [rel CommandID], al

    ; Dispatch command
    cmp al, Vs2Ds_SendRegisters
    je .send_regs

    cmp al, Vs2Ds_SendMemory
    je .send_mem

    cmp al, Vs2Ds_SendFrame
    je .send_frm

    cmp al, Vs2Ds_BreakOnAddress
    je .set_bp

    cmp al, Vs2Ds_SetINT3
    je .set_int3

    cmp al, Vs2Ds_ClearINT3
    je .clear_int3

    cmp al, Vs2Ds_Break
    je .do_brk

    ; Unknown or control commands - just return
    jmp .done

.send_regs:
    call send_registers
    call send_ack
    jmp .done

.send_mem:
    call send_memory
    call send_ack
    jmp .done

.send_frm:
    call send_frame
    call send_ack
    jmp .done

.set_bp:
    ; Read BP ID
    call serial_read_byte
    movzx rdi, al
    ; Read address
    call serial_read_qword
    mov rsi, rax
    call _debug_stub_set_breakpoint
    call send_ack
    jmp .done

.set_int3:
    ; Read address
    call serial_read_qword
    mov byte [rax], 0xCC
    call send_ack
    jmp .done

.clear_int3:
    ; Read address
    call serial_read_qword
    mov byte [rax], 0x90
    call send_ack
    jmp .done

.do_brk:
    call do_break
    jmp .done

.done:
    pop rdi
    pop rsi
    pop rdx
    pop rcx
    pop rbx
    pop rax
    ret

; ============================================================================
; Debug Stub Break Handler (called from INT3 exception)
; ============================================================================

; void debug_stub_break()
; Should be called from INT3 handler with saved register state
_debug_stub_break:
    call save_all_registers
    call do_break
    ret
