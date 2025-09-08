OUTPUT_FORMAT(elf64-littleriscv)

ENTRY(kmain)

PHDRS
{
    text PT_LOAD;
    rodata PT_LOAD;
    data PT_LOAD;
}

SECTIONS
{
    /* RISC-V kernel typically starts at 0x80200000 for QEMU virt machine */
    . = 0xffffffe000200000;

    . = ALIGN(4K);

    .text : {
        *(.text.init)
        *(.text .text.*)
    } :text

    . = ALIGN(4K);

    .rodata : {
        *(.rodata .rodata.*)
    } :rodata

    . = ALIGN(4K);

    .data : {
        *(.data .data.*)
        . = ALIGN(8);
        PROVIDE(__global_pointer$ = . + 0x800);
        *(.sdata .sdata.*)
    } :data

    .bss : {
        __bss_start = .;
        *(.sbss .sbss.*)
        *(.bss .bss.*)
        *(COMMON)
        __bss_end = .;
    } :data

    /DISCARD/ : {
        *(.eh_frame*)
        *(.note .note.*)
        *(.comment)
    }
}