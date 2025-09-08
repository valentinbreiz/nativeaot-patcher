OUTPUT_FORMAT(elf64-aarch64)

ENTRY(kmain)

PHDRS
{
    text PT_LOAD;
    rodata PT_LOAD;
    data PT_LOAD;
}

SECTIONS
{
    /* ARM64 kernel typically starts at 0x80000 for Raspberry Pi */
    . = 0xffff000000080000;

    . = ALIGN(4K);

    .text : {
        *(.text.boot)
        *(.text .text.*)
    } :text

    . = ALIGN(4K);

    .rodata : {
        *(.rodata .rodata.*)
    } :rodata

    . = ALIGN(4K);

    .data : {
        *(.data .data.*)
    } :data

    .bss ALIGN(16) : {
        __bss_start = .;
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