global irq0_stub
global irq1_stub
global irq2_stub
global irq3_stub
global irq4_stub
global irq5_stub
global irq6_stub
global irq7_stub
global irq8_stub
global irq9_stub
global irq10_stub
global irq11_stub
global irq12_stub
global irq13_stub
global irq14_stub
global irq15_stub
global irq16_stub
global irq17_stub
global irq18_stub
global irq19_stub
global irq20_stub
global irq21_stub
global irq22_stub
global irq23_stub
global irq24_stub
global irq25_stub
global irq26_stub
global irq27_stub
global irq28_stub
global irq29_stub
global irq30_stub
global irq31_stub
global irq32_stub
global irq33_stub
global irq34_stub
global irq35_stub
global irq36_stub
global irq37_stub
global irq38_stub
global irq39_stub
global irq40_stub
global irq41_stub
global irq42_stub
global irq43_stub
global irq44_stub
global irq45_stub
global irq46_stub
global irq47_stub
global irq48_stub
global irq49_stub
global irq50_stub
global irq51_stub
global irq52_stub
global irq53_stub
global irq54_stub
global irq55_stub
global irq56_stub
global irq57_stub
global irq58_stub
global irq59_stub
global irq60_stub
global irq61_stub
global irq62_stub
global irq63_stub
global irq64_stub
global irq65_stub
global irq66_stub
global irq67_stub
global irq68_stub
global irq69_stub
global irq70_stub
global irq71_stub
global irq72_stub
global irq73_stub
global irq74_stub
global irq75_stub
global irq76_stub
global irq77_stub
global irq78_stub
global irq79_stub
global irq80_stub
global irq81_stub
global irq82_stub
global irq83_stub
global irq84_stub
global irq85_stub
global irq86_stub
global irq87_stub
global irq88_stub
global irq89_stub
global irq90_stub
global irq91_stub
global irq92_stub
global irq93_stub
global irq94_stub
global irq95_stub
global irq96_stub
global irq97_stub
global irq98_stub
global irq99_stub
global irq100_stub
global irq101_stub
global irq102_stub
global irq103_stub
global irq104_stub
global irq105_stub
global irq106_stub
global irq107_stub
global irq108_stub
global irq109_stub
global irq110_stub
global irq111_stub
global irq112_stub
global irq113_stub
global irq114_stub
global irq115_stub
global irq116_stub
global irq117_stub
global irq118_stub
global irq119_stub
global irq120_stub
global irq121_stub
global irq122_stub
global irq123_stub
global irq124_stub
global irq125_stub
global irq126_stub
global irq127_stub
global irq128_stub
global irq129_stub
global irq130_stub
global irq131_stub
global irq132_stub
global irq133_stub
global irq134_stub
global irq135_stub
global irq136_stub
global irq137_stub
global irq138_stub
global irq139_stub
global irq140_stub
global irq141_stub
global irq142_stub
global irq143_stub
global irq144_stub
global irq145_stub
global irq146_stub
global irq147_stub
global irq148_stub
global irq149_stub
global irq150_stub
global irq151_stub
global irq152_stub
global irq153_stub
global irq154_stub
global irq155_stub
global irq156_stub
global irq157_stub
global irq158_stub
global irq159_stub
global irq160_stub
global irq161_stub
global irq162_stub
global irq163_stub
global irq164_stub
global irq165_stub
global irq166_stub
global irq167_stub
global irq168_stub
global irq169_stub
global irq170_stub
global irq171_stub
global irq172_stub
global irq173_stub
global irq174_stub
global irq175_stub
global irq176_stub
global irq177_stub
global irq178_stub
global irq179_stub
global irq180_stub
global irq181_stub
global irq182_stub
global irq183_stub
global irq184_stub
global irq185_stub
global irq186_stub
global irq187_stub
global irq188_stub
global irq189_stub
global irq190_stub
global irq191_stub
global irq192_stub
global irq193_stub
global irq194_stub
global irq195_stub
global irq196_stub
global irq197_stub
global irq198_stub
global irq199_stub
global irq200_stub
global irq201_stub
global irq202_stub
global irq203_stub
global irq204_stub
global irq205_stub
global irq206_stub
global irq207_stub
global irq208_stub
global irq209_stub
global irq210_stub
global irq211_stub
global irq212_stub
global irq213_stub
global irq214_stub
global irq215_stub
global irq216_stub
global irq217_stub
global irq218_stub
global irq219_stub
global irq220_stub
global irq221_stub
global irq222_stub
global irq223_stub
global irq224_stub
global irq225_stub
global irq226_stub
global irq227_stub
global irq228_stub
global irq229_stub
global irq230_stub
global irq231_stub
global irq232_stub
global irq233_stub
global irq234_stub
global irq235_stub
global irq236_stub
global irq237_stub
global irq238_stub
global irq239_stub
global irq240_stub
global irq241_stub
global irq242_stub
global irq243_stub
global irq244_stub
global irq245_stub
global irq246_stub
global irq247_stub
global irq248_stub
global irq249_stub
global irq250_stub
global irq251_stub
global irq252_stub
global irq253_stub
global irq254_stub
global irq255_stub

global _native_x64_irq_table
extern __managed__irq

section .text

; void lidt_native(IdtPointer* ptr)
global _native_x64_load_idt
_native_x64_load_idt:
    cli
    lidt [rdi]
    sti
    ret

; uint64_t _native_x64_get_code_selector()
; Returns the current CS (code segment selector) value
global _native_x64_get_code_selector
_native_x64_get_code_selector:
    xor rax, rax
    mov ax, cs
    ret

; void _native_x64_test_int32()
; Triggers INT 32 to test if interrupt stubs work
global _native_x64_test_int32
_native_x64_test_int32:
    ; Execute INT 32 - should call our stub
    int 32
    ret

; void _native_x64_set_context_switch_rsp(nuint newRsp)
; Sets the target RSP for context switch. Called from managed code
; during timer interrupt to request a context switch.
; rdi = new RSP to switch to (pointing to saved context)
global _native_x64_set_context_switch_rsp
_native_x64_set_context_switch_rsp:
    mov [rel _context_switch_target_rsp], rdi
    ret

; nuint _native_x64_get_context_switch_rsp()
; Gets the current context switch target RSP (for debugging)
global _native_x64_get_context_switch_rsp
_native_x64_get_context_switch_rsp:
    mov rax, [rel _context_switch_target_rsp]
    ret

; nuint _native_x64_get_rsp()
; Gets the current RSP value
global _native_x64_get_rsp
_native_x64_get_rsp:
    mov rax, rsp
    ret

; void _native_x64_set_context_switch_new_thread(int isNew)
; Sets whether the target thread is NEW (1) or RESUMED (0)
; rdi = isNew flag
global _native_x64_set_context_switch_new_thread
_native_x64_set_context_switch_new_thread:
    mov [rel _context_switch_is_new_thread], rdi
    ret

section .bss
; Per-CPU context switch target RSP (0 = no switch, non-zero = switch to this RSP)
; For SMP, this would be per-CPU, but for now single-CPU
global _context_switch_target_rsp
_context_switch_target_rsp: resq 1

; Flag indicating if the target thread is NEW (1) or RESUMED (0)
; NEW threads need RSP loaded from context, RESUMED threads use iretq
global _context_switch_is_new_thread
_context_switch_is_new_thread: resq 1

; Temporary storage for is_new_thread flag during restore
; Used to avoid destroying RAX when checking if this is a new thread
_temp_is_new_thread: resq 1

section .data
_native_x64_irq_table:
dq irq0_stub
dq irq1_stub
dq irq2_stub
dq irq3_stub
dq irq4_stub
dq irq5_stub
dq irq6_stub
dq irq7_stub
dq irq8_stub
dq irq9_stub
dq irq10_stub
dq irq11_stub
dq irq12_stub
dq irq13_stub
dq irq14_stub
dq irq15_stub
dq irq16_stub
dq irq17_stub
dq irq18_stub
dq irq19_stub
dq irq20_stub
dq irq21_stub
dq irq22_stub
dq irq23_stub
dq irq24_stub
dq irq25_stub
dq irq26_stub
dq irq27_stub
dq irq28_stub
dq irq29_stub
dq irq30_stub
dq irq31_stub
dq irq32_stub
dq irq33_stub
dq irq34_stub
dq irq35_stub
dq irq36_stub
dq irq37_stub
dq irq38_stub
dq irq39_stub
dq irq40_stub
dq irq41_stub
dq irq42_stub
dq irq43_stub
dq irq44_stub
dq irq45_stub
dq irq46_stub
dq irq47_stub
dq irq48_stub
dq irq49_stub
dq irq50_stub
dq irq51_stub
dq irq52_stub
dq irq53_stub
dq irq54_stub
dq irq55_stub
dq irq56_stub
dq irq57_stub
dq irq58_stub
dq irq59_stub
dq irq60_stub
dq irq61_stub
dq irq62_stub
dq irq63_stub
dq irq64_stub
dq irq65_stub
dq irq66_stub
dq irq67_stub
dq irq68_stub
dq irq69_stub
dq irq70_stub
dq irq71_stub
dq irq72_stub
dq irq73_stub
dq irq74_stub
dq irq75_stub
dq irq76_stub
dq irq77_stub
dq irq78_stub
dq irq79_stub
dq irq80_stub
dq irq81_stub
dq irq82_stub
dq irq83_stub
dq irq84_stub
dq irq85_stub
dq irq86_stub
dq irq87_stub
dq irq88_stub
dq irq89_stub
dq irq90_stub
dq irq91_stub
dq irq92_stub
dq irq93_stub
dq irq94_stub
dq irq95_stub
dq irq96_stub
dq irq97_stub
dq irq98_stub
dq irq99_stub
dq irq100_stub
dq irq101_stub
dq irq102_stub
dq irq103_stub
dq irq104_stub
dq irq105_stub
dq irq106_stub
dq irq107_stub
dq irq108_stub
dq irq109_stub
dq irq110_stub
dq irq111_stub
dq irq112_stub
dq irq113_stub
dq irq114_stub
dq irq115_stub
dq irq116_stub
dq irq117_stub
dq irq118_stub
dq irq119_stub
dq irq120_stub
dq irq121_stub
dq irq122_stub
dq irq123_stub
dq irq124_stub
dq irq125_stub
dq irq126_stub
dq irq127_stub
dq irq128_stub
dq irq129_stub
dq irq130_stub
dq irq131_stub
dq irq132_stub
dq irq133_stub
dq irq134_stub
dq irq135_stub
dq irq136_stub
dq irq137_stub
dq irq138_stub
dq irq139_stub
dq irq140_stub
dq irq141_stub
dq irq142_stub
dq irq143_stub
dq irq144_stub
dq irq145_stub
dq irq146_stub
dq irq147_stub
dq irq148_stub
dq irq149_stub
dq irq150_stub
dq irq151_stub
dq irq152_stub
dq irq153_stub
dq irq154_stub
dq irq155_stub
dq irq156_stub
dq irq157_stub
dq irq158_stub
dq irq159_stub
dq irq160_stub
dq irq161_stub
dq irq162_stub
dq irq163_stub
dq irq164_stub
dq irq165_stub
dq irq166_stub
dq irq167_stub
dq irq168_stub
dq irq169_stub
dq irq170_stub
dq irq171_stub
dq irq172_stub
dq irq173_stub
dq irq174_stub
dq irq175_stub
dq irq176_stub
dq irq177_stub
dq irq178_stub
dq irq179_stub
dq irq180_stub
dq irq181_stub
dq irq182_stub
dq irq183_stub
dq irq184_stub
dq irq185_stub
dq irq186_stub
dq irq187_stub
dq irq188_stub
dq irq189_stub
dq irq190_stub
dq irq191_stub
dq irq192_stub
dq irq193_stub
dq irq194_stub
dq irq195_stub
dq irq196_stub
dq irq197_stub
dq irq198_stub
dq irq199_stub
dq irq200_stub
dq irq201_stub
dq irq202_stub
dq irq203_stub
dq irq204_stub
dq irq205_stub
dq irq206_stub
dq irq207_stub
dq irq208_stub
dq irq209_stub
dq irq210_stub
dq irq211_stub
dq irq212_stub
dq irq213_stub
dq irq214_stub
dq irq215_stub
dq irq216_stub
dq irq217_stub
dq irq218_stub
dq irq219_stub
dq irq220_stub
dq irq221_stub
dq irq222_stub
dq irq223_stub
dq irq224_stub
dq irq225_stub
dq irq226_stub
dq irq227_stub
dq irq228_stub
dq irq229_stub
dq irq230_stub
dq irq231_stub
dq irq232_stub
dq irq233_stub
dq irq234_stub
dq irq235_stub
dq irq236_stub
dq irq237_stub
dq irq238_stub
dq irq239_stub
dq irq240_stub
dq irq241_stub
dq irq242_stub
dq irq243_stub
dq irq244_stub
dq irq245_stub
dq irq246_stub
dq irq247_stub
dq irq248_stub
dq irq249_stub
dq irq250_stub
dq irq251_stub
dq irq252_stub
dq irq253_stub
dq irq254_stub
dq irq255_stub


section .text

; nint _native_x64_get_irq_stub(int index)
; Returns the address of the IRQ stub for the given vector index (0-255)
; rdi = index (first argument in x86-64 calling convention)
; Returns address in rax
global _native_x64_get_irq_stub
_native_x64_get_irq_stub:
    ; Load the address from the irq_table lookup array
    ; The table is in .data section and has 256 entries (8 bytes each)
    lea rax, [rel _native_x64_irq_table]
    mov rax, [rax + rdi * 8]  ; Load stub address for vector index
    ret

%macro IRQ_STUB 1
irq%1_stub:
    ; === SAVE CONTEXT ===
    ; CPU pushes: RIP, CS, RFLAGS (and RSP, SS if privilege change)
    ; Stack at entry: [RSP] = RIP, [RSP+8] = CS, [RSP+16] = RFLAGS
    ;
    ; ThreadContext struct layout (low to high address):
    ;   XMM[256], R15..RAX (15 regs), Interrupt, CpuFlags, Cr2, TempRcx, RIP, CS, RFLAGS, [RSP, SS]
    ;
    ; Strategy: Save all GPRs first, then compute and insert context info

    ; Save ALL GPRs immediately (in reverse struct order so RAX ends up at correct position)
    push rax
    push rcx
    push rdx
    push rbx
    push rbp
    push rsi
    push rdi
    push r8
    push r9
    push r10
    push r11
    push r12
    push r13
    push r14
    push r15
    ; 15 GPRs = 120 bytes
    ; Stack: [rsp+0..119] = R15..RAX, [rsp+120] = RIP, [rsp+128] = CS, [rsp+136] = RFLAGS

    ; Now use rax/rcx as scratch (originals are saved on stack)
    mov rax, [rsp + 136]         ; RFLAGS from CPU frame
    mov rcx, cr2                 ; Page fault address

    ; We need to insert 32 bytes of context info between GPRs and CPU frame
    ; Move GPRs down by 32 bytes to make room
    ; This is done by copying each GPR down

    ; First, push the context info values (they'll be in wrong place temporarily)
    push 0                       ; TempRcx placeholder
    push rcx                     ; Cr2
    push rax                     ; CpuFlags
    mov rax, %1                  ; Load interrupt number into register
    push rax                     ; Interrupt
    ; Added 32 bytes, now rsp is 32 bytes lower
    ; Stack: [rsp+0..31] = context info (wrong position)
    ;        [rsp+32..151] = R15..RAX (GPRs)
    ;        [rsp+152] = RIP...

    ; Now move GPRs down by 32 bytes to their correct position
    ; Read from [rsp+32+offset] and write to [rsp+offset]
    mov rax, [rsp + 32]          ; R15
    mov [rsp + 0], rax
    mov rax, [rsp + 40]          ; R14
    mov [rsp + 8], rax
    mov rax, [rsp + 48]          ; R13
    mov [rsp + 16], rax
    mov rax, [rsp + 56]          ; R12
    mov [rsp + 24], rax
    mov rax, [rsp + 64]          ; R11
    mov [rsp + 32], rax
    mov rax, [rsp + 72]          ; R10
    mov [rsp + 40], rax
    mov rax, [rsp + 80]          ; R9
    mov [rsp + 48], rax
    mov rax, [rsp + 88]          ; R8
    mov [rsp + 56], rax
    mov rax, [rsp + 96]          ; RDI
    mov [rsp + 64], rax
    mov rax, [rsp + 104]         ; RSI
    mov [rsp + 72], rax
    mov rax, [rsp + 112]         ; RBP
    mov [rsp + 80], rax
    mov rax, [rsp + 120]         ; RBX
    mov [rsp + 88], rax
    mov rax, [rsp + 128]         ; RDX
    mov [rsp + 96], rax
    mov rax, [rsp + 136]         ; RCX
    mov [rsp + 104], rax
    mov rax, [rsp + 144]         ; RAX
    mov [rsp + 112], rax

    ; Now write context info at correct position (after GPRs)
    ; Context info should be at [rsp+120] to [rsp+151]
    mov rax, %1                  ; Load interrupt number into register
    mov [rsp + 120], rax         ; Interrupt
    mov rax, [rsp + 168]         ; Read RFLAGS again (it moved: was at 136, now at 136+32=168)
    mov [rsp + 128], rax         ; CpuFlags
    mov rax, cr2
    mov [rsp + 136], rax         ; Cr2
    xor rax, rax
    mov [rsp + 144], rax         ; TempRcx (zero)

    ; Stack is now correctly laid out:
    ; [rsp+0..119] = R15..RAX (GPRs, 120 bytes)
    ; [rsp+120..151] = Interrupt, CpuFlags, Cr2, TempRcx (32 bytes)
    ; [rsp+152..] = RIP, CS, RFLAGS (CPU frame)

    ; Save XMM registers (SSE/SIMD state) - 16 registers * 16 bytes = 256 bytes
    sub rsp, 256
    movdqu [rsp + 0], xmm0
    movdqu [rsp + 16], xmm1
    movdqu [rsp + 32], xmm2
    movdqu [rsp + 48], xmm3
    movdqu [rsp + 64], xmm4
    movdqu [rsp + 80], xmm5
    movdqu [rsp + 96], xmm6
    movdqu [rsp + 112], xmm7
    movdqu [rsp + 128], xmm8
    movdqu [rsp + 144], xmm9
    movdqu [rsp + 160], xmm10
    movdqu [rsp + 176], xmm11
    movdqu [rsp + 192], xmm12
    movdqu [rsp + 208], xmm13
    movdqu [rsp + 224], xmm14
    movdqu [rsp + 240], xmm15

    ; === CALL HANDLER ===
    lea rdi, [rsp + 256]
    call __managed__irq

    ; === CHECK FOR CONTEXT SWITCH ===
    mov rax, [rel _context_switch_target_rsp]
    test rax, rax
    jz .restore%1

    ; Switch to new context - clear flags and switch stack
    xor rcx, rcx
    mov [rel _context_switch_target_rsp], rcx

    ; Save the is_new_thread flag before clearing (we need it after restore)
    mov rdx, [rel _context_switch_is_new_thread]
    mov [rel _context_switch_is_new_thread], rcx

    ; Switch stack
    mov rsp, rax

    ; Store flag in a location we can read after GPR restore
    ; Use the TempRcx slot in the context (offset 400 from start, or after GPRs+info)
    ; After XMM restore: rsp points to GPRs
    ; GPRs = 120 bytes, then info section starts
    ; Interrupt(8) + CpuFlags(8) + Cr2(8) + TempRcx(8)
    ; So TempRcx is at offset 256 + 120 + 24 = 400 from original RSP
    ; Or from current RSP (pointing to XMM): 120 + 24 = 144 bytes after XMM area
    mov [rsp + 256 + 120 + 24], rdx  ; Store in TempRcx slot

.restore%1:
    ; === RESTORE CONTEXT (single path for all cases) ===
    ; First, read TempRcx BEFORE restoring GPRs (so we don't destroy RAX)
    ; TempRcx is at: XMM(256) + GPRs(120) + interrupt(8) + cpuflags(8) + cr2(8) = offset 400
    mov rax, [rsp + 256 + 120 + 24]  ; Read TempRcx (is_new_thread flag)
    mov [rel _temp_is_new_thread], rax  ; Save it in a global variable

    ; Restore XMM
    movdqu xmm0, [rsp + 0]
    movdqu xmm1, [rsp + 16]
    movdqu xmm2, [rsp + 32]
    movdqu xmm3, [rsp + 48]
    movdqu xmm4, [rsp + 64]
    movdqu xmm5, [rsp + 80]
    movdqu xmm6, [rsp + 96]
    movdqu xmm7, [rsp + 112]
    movdqu xmm8, [rsp + 128]
    movdqu xmm9, [rsp + 144]
    movdqu xmm10, [rsp + 160]
    movdqu xmm11, [rsp + 176]
    movdqu xmm12, [rsp + 192]
    movdqu xmm13, [rsp + 208]
    movdqu xmm14, [rsp + 224]
    movdqu xmm15, [rsp + 240]
    add rsp, 256

    ; Restore GPRs
    pop r15
    pop r14
    pop r13
    pop r12
    pop r11
    pop r10
    pop r9
    pop r8
    pop rdi
    pop rsi
    pop rbp
    pop rbx
    pop rdx
    pop rcx
    pop rax

    ; Skip all context info: interrupt(8) + cpu_flags(8) + cr2(8) + temprx(8) = 32 bytes
    add rsp, 32

    ; === EXIT PATH ===
    ; Stack now: RIP, CS, RFLAGS, [RSP, SS if privilege change]
    ; Check saved flag for new thread
    cmp qword [rel _temp_is_new_thread], 0
    jnz .new_thread%1

    ; RESUMED thread or normal return - use iretq
    iretq

.new_thread%1:
    ; NEW thread - need to set up RSP from context and jump
    ; Stack: RIP, CS, RFLAGS, RSP, SS
    pop r11          ; RIP → r11
    add rsp, 8       ; skip CS
    popfq            ; restore RFLAGS (enables interrupts)
    pop rsp          ; load thread's stack pointer
    jmp r11          ; jump to entry point

%endmacro

; Generate IRQ stubs
IRQ_STUB 0
IRQ_STUB 1
IRQ_STUB 2
IRQ_STUB 3
IRQ_STUB 4
IRQ_STUB 5
IRQ_STUB 6
IRQ_STUB 7
IRQ_STUB 8
IRQ_STUB 9
IRQ_STUB 10
IRQ_STUB 11
IRQ_STUB 12
IRQ_STUB 13
IRQ_STUB 14
IRQ_STUB 15
IRQ_STUB 16
IRQ_STUB 17
IRQ_STUB 18
IRQ_STUB 19
IRQ_STUB 20
IRQ_STUB 21
IRQ_STUB 22
IRQ_STUB 23
IRQ_STUB 24
IRQ_STUB 25
IRQ_STUB 26
IRQ_STUB 27
IRQ_STUB 28
IRQ_STUB 29
IRQ_STUB 30
IRQ_STUB 31
IRQ_STUB 32
IRQ_STUB 33
IRQ_STUB 34
IRQ_STUB 35
IRQ_STUB 36
IRQ_STUB 37
IRQ_STUB 38
IRQ_STUB 39
IRQ_STUB 40
IRQ_STUB 41
IRQ_STUB 42
IRQ_STUB 43
IRQ_STUB 44
IRQ_STUB 45
IRQ_STUB 46
IRQ_STUB 47
IRQ_STUB 48
IRQ_STUB 49
IRQ_STUB 50
IRQ_STUB 51
IRQ_STUB 52
IRQ_STUB 53
IRQ_STUB 54
IRQ_STUB 55
IRQ_STUB 56
IRQ_STUB 57
IRQ_STUB 58
IRQ_STUB 59
IRQ_STUB 60
IRQ_STUB 61
IRQ_STUB 62
IRQ_STUB 63
IRQ_STUB 64
IRQ_STUB 65
IRQ_STUB 66
IRQ_STUB 67
IRQ_STUB 68
IRQ_STUB 69
IRQ_STUB 70
IRQ_STUB 71
IRQ_STUB 72
IRQ_STUB 73
IRQ_STUB 74
IRQ_STUB 75
IRQ_STUB 76
IRQ_STUB 77
IRQ_STUB 78
IRQ_STUB 79
IRQ_STUB 80
IRQ_STUB 81
IRQ_STUB 82
IRQ_STUB 83
IRQ_STUB 84
IRQ_STUB 85
IRQ_STUB 86
IRQ_STUB 87
IRQ_STUB 88
IRQ_STUB 89
IRQ_STUB 90
IRQ_STUB 91
IRQ_STUB 92
IRQ_STUB 93
IRQ_STUB 94
IRQ_STUB 95
IRQ_STUB 96
IRQ_STUB 97
IRQ_STUB 98
IRQ_STUB 99
IRQ_STUB 100
IRQ_STUB 101
IRQ_STUB 102
IRQ_STUB 103
IRQ_STUB 104
IRQ_STUB 105
IRQ_STUB 106
IRQ_STUB 107
IRQ_STUB 108
IRQ_STUB 109
IRQ_STUB 110
IRQ_STUB 111
IRQ_STUB 112
IRQ_STUB 113
IRQ_STUB 114
IRQ_STUB 115
IRQ_STUB 116
IRQ_STUB 117
IRQ_STUB 118
IRQ_STUB 119
IRQ_STUB 120
IRQ_STUB 121
IRQ_STUB 122
IRQ_STUB 123
IRQ_STUB 124
IRQ_STUB 125
IRQ_STUB 126
IRQ_STUB 127
IRQ_STUB 128
IRQ_STUB 129
IRQ_STUB 130
IRQ_STUB 131
IRQ_STUB 132
IRQ_STUB 133
IRQ_STUB 134
IRQ_STUB 135
IRQ_STUB 136
IRQ_STUB 137
IRQ_STUB 138
IRQ_STUB 139
IRQ_STUB 140
IRQ_STUB 141
IRQ_STUB 142
IRQ_STUB 143
IRQ_STUB 144
IRQ_STUB 145
IRQ_STUB 146
IRQ_STUB 147
IRQ_STUB 148
IRQ_STUB 149
IRQ_STUB 150
IRQ_STUB 151
IRQ_STUB 152
IRQ_STUB 153
IRQ_STUB 154
IRQ_STUB 155
IRQ_STUB 156
IRQ_STUB 157
IRQ_STUB 158
IRQ_STUB 159
IRQ_STUB 160
IRQ_STUB 161
IRQ_STUB 162
IRQ_STUB 163
IRQ_STUB 164
IRQ_STUB 165
IRQ_STUB 166
IRQ_STUB 167
IRQ_STUB 168
IRQ_STUB 169
IRQ_STUB 170
IRQ_STUB 171
IRQ_STUB 172
IRQ_STUB 173
IRQ_STUB 174
IRQ_STUB 175
IRQ_STUB 176
IRQ_STUB 177
IRQ_STUB 178
IRQ_STUB 179
IRQ_STUB 180
IRQ_STUB 181
IRQ_STUB 182
IRQ_STUB 183
IRQ_STUB 184
IRQ_STUB 185
IRQ_STUB 186
IRQ_STUB 187
IRQ_STUB 188
IRQ_STUB 189
IRQ_STUB 190
IRQ_STUB 191
IRQ_STUB 192
IRQ_STUB 193
IRQ_STUB 194
IRQ_STUB 195
IRQ_STUB 196
IRQ_STUB 197
IRQ_STUB 198
IRQ_STUB 199
IRQ_STUB 200
IRQ_STUB 201
IRQ_STUB 202
IRQ_STUB 203
IRQ_STUB 204
IRQ_STUB 205
IRQ_STUB 206
IRQ_STUB 207
IRQ_STUB 208
IRQ_STUB 209
IRQ_STUB 210
IRQ_STUB 211
IRQ_STUB 212
IRQ_STUB 213
IRQ_STUB 214
IRQ_STUB 215
IRQ_STUB 216
IRQ_STUB 217
IRQ_STUB 218
IRQ_STUB 219
IRQ_STUB 220
IRQ_STUB 221
IRQ_STUB 222
IRQ_STUB 223
IRQ_STUB 224
IRQ_STUB 225
IRQ_STUB 226
IRQ_STUB 227
IRQ_STUB 228
IRQ_STUB 229
IRQ_STUB 230
IRQ_STUB 231
IRQ_STUB 232
IRQ_STUB 233
IRQ_STUB 234
IRQ_STUB 235
IRQ_STUB 236
IRQ_STUB 237
IRQ_STUB 238
IRQ_STUB 239
IRQ_STUB 240
IRQ_STUB 241
IRQ_STUB 242
IRQ_STUB 243
IRQ_STUB 244
IRQ_STUB 245
IRQ_STUB 246
IRQ_STUB 247
IRQ_STUB 248
IRQ_STUB 249
IRQ_STUB 250
IRQ_STUB 251
IRQ_STUB 252
IRQ_STUB 253
IRQ_STUB 254
IRQ_STUB 255
