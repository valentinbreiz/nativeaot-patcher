// x64 Global Descriptor Table - custom kernel GDT with ring-0 AND ring-3
// 64-bit descriptors so SYSCALL/SYSRET and iretq can drop to ring 3.
//
// Limine's boot GDT only carries ring-0 64-bit descriptors; real ring-3
// needs user code/data selectors at DPL=3 (CS=0x1B, SS=0x23). This file
// installs a fixed GDT early in HAL init, before Idt.RegisterAllInterrupts
// reads CS for the IDT selectors (the reloaded CS is 0x08, matching what
// the bootloader set, so IDT entries are unchanged).
//
// Long-mode system descriptors are 16 bytes (the high 8 bytes hold the
// L/Type/limit/base high bits); the entries below are the standard set
// used by Linux/etc. for a 64-bit kernel:
//   0x00 null
//   0x08 ring-0 64-bit code  (L=1, type=0x9A, present, DPL=0)
//   0x10 ring-0 64-bit data  (type=0x92, present, DPL=0)
//   0x1B ring-3 64-bit code  (L=1, type=0xFA, present, DPL=3)
//   0x23 ring-3 64-bit data  (type=0xF2, present, DPL=3)

.intel_syntax noprefix

.data
.balign 16
.global _native_x64_gdt
_native_x64_gdt:
    .quad 0x0                          // 0x00 null
    .quad 0x00AF9A000000FFFF          // 0x08 ring-0 64-bit code
    .quad 0x00CF92000000FFFF          // 0x10 ring-0 64-bit data
    .quad 0x00AFFA000000FFFF          // 0x1B ring-3 64-bit code
    .quad 0x00CFF2000000FFFF          // 0x23 ring-3 64-bit data

// Far pointer used by the reload path below. 10-byte logical address
// (8-byte offset, 2-byte selector), aligned/padded to 16.
.balign 8
_native_x64_reload_target:
    .quad _native_x64_gdt_reload_cs
    .short 0x08

// GDT register image loaded by lgdt: 16-bit limit, 64-bit base.
.balign 8
.global _native_x64_gdt_pointer
_native_x64_gdt_pointer:
    .short 39                          // sizeof(gdt)-1 = 5*8-1
    .quad _native_x64_gdt

.text

// ----------------------------------------------------------------------------
// CS-reload trampoline reached via the far jump. At entry here the new GDT
// is active and CS=0x08. Reload the data segments to the ring-0 data
// selector (0x10) and return to the caller. Caller-saved only.
// ----------------------------------------------------------------------------
.global _native_x64_gdt_reload_cs
_native_x64_gdt_reload_cs:
    xor     ax, ax
    mov     ax, 0x10
    mov     ss, ax
    mov     ds, ax
    mov     es, ax
    ret

// ----------------------------------------------------------------------------
// void _native_x64_load_gdt(void)
// Loads the kernel GDT (the _native_x64_gdt_pointer image in .data above),
// reloads CS=0x08 via a far jump to 0x08:_native_x64_gdt_reload_cs, then
// restores the data segments from 0x10. Interrupts are disabled on entry;
// the caller re-enables them once the IDT has been (re)loaded.
// ----------------------------------------------------------------------------
.global _native_x64_load_gdt
_native_x64_load_gdt:
    cli
    lgdt    [rip + _native_x64_gdt_pointer]

    // Reload data segments from the new GDT immediately (the old selectors
    // might not be valid in the new table layout).
    xor     ax, ax
    mov     ax, 0x10
    mov     ss, ax
    mov     ds, ax
    mov     es, ax

    // Far-jump to reload CS=0x08. clang's intel_syntax has no clean form for
    // an indirect far jump ("jmp far [rax]" is misparsed as a near indirect
    // jump with "far" as a displacement symbol), so switch to AT&T for the
    // ljmp and back. 0x08:_native_x64_gdt_reload_cs is encoded as the
    // 8:2 logical-address pointer in .data above.
    lea     rax, [rip + _native_x64_reload_target]
    .att_syntax prefix
    ljmpq    *(%rax)
    .intel_syntax noprefix

    // _native_x64_gdt_reload_cs returns here.
    ret
