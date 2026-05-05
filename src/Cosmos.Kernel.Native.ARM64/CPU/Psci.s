.global _native_psci_system_off
.global _native_psci_system_reset

.text
.align 4

// PSCI 0.2+ via HVC (QEMU virt default conduit).
// On real hardware running at EL1 under EL3 firmware the conduit may be SMC;
// not supported here. Function IDs are SMC32 calling convention.

// SYSTEM_OFF = 0x84000008
_native_psci_system_off:
    mov     x0, #0x84000000
    movk    x0, #0x0008
    mov     x1, xzr
    mov     x2, xzr
    mov     x3, xzr
    hvc     #0
    b       .

// SYSTEM_RESET = 0x84000009
_native_psci_system_reset:
    mov     x0, #0x84000000
    movk    x0, #0x0009
    mov     x1, xzr
    mov     x2, xzr
    mov     x3, xzr
    hvc     #0
    b       .
