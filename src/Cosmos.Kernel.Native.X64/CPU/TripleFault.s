.intel_syntax noprefix

.global _native_cpu_triple_fault

.text

// Last-ditch reboot: load a null IDTR then trigger an interrupt.
// CPU has no handler -> #DF -> #DF -> triple fault -> processor reset.
_native_cpu_triple_fault:
    lidt    [rip + .Lnull_idt]
    int     3
.Lhang:
    hlt
    jmp     .Lhang

.align 8
.Lnull_idt:
    .word   0       // limit = 0
    .quad   0       // base  = 0
