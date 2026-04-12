; x86_64 page table helpers for modifying the CR3 pagemap Limine hands off.
; Used by Cosmos.Kernel.Core.Memory.DeviceMapper to install Device MMIO entries
; into the kernel's higher-half pagemap (LAPIC, IOAPIC, HPET, etc.).

global _native_x64_read_cr3
global _native_x64_invlpg
global _native_x64_spare_pd_table_addr

; Pre-allocated PD table (4 KiB, 512 entries) for splitting a 1 GiB PDPT block
; into 2 MiB PD entries. One is sufficient for the set of MMIO regions Cosmos
; installs from early boot (LAPIC, IOAPIC) — both sit in the same 1 GiB range
; on the typical QEMU x86_64 layout and reuse the same split PD.
section .bss
    align 4096
spare_pd_table:
    resb 4096

section .text

; ulong _native_x64_read_cr3(void)
; Returns the current CR3 value (PML4 physical address + flags).
_native_x64_read_cr3:
    mov     rax, cr3
    ret

; void _native_x64_invlpg(ulong va)
; Invalidates the TLB entry for the given linear address.
; System V ABI: first argument in RDI.
_native_x64_invlpg:
    invlpg  [rdi]
    ret

; ulong _native_x64_spare_pd_table_addr(void)
; Returns the linker address (virtual in higher-half mapping) of the
; pre-allocated spare PD table.
_native_x64_spare_pd_table_addr:
    lea     rax, [rel spare_pd_table]
    ret
