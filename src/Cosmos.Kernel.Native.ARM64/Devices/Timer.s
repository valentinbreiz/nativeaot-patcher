// ARM64 Generic Timer native functions
// Access to CNTP_* (physical timer) registers

.global _native_arm64_timer_get_frequency
.global _native_arm64_timer_get_counter
.global _native_arm64_timer_set_compare
.global _native_arm64_timer_enable
.global _native_arm64_timer_disable
.global _native_arm64_timer_set_tval
.global _native_arm64_timer_get_ctl
.global _native_arm64_vtimer_enable
.global _native_arm64_vtimer_disable
.global _native_arm64_vtimer_set_tval
.global _native_arm64_vtimer_get_ctl
.global _native_arm64_get_current_el
.global _native_arm64_timer_enable_user_access
.global _native_arm64_htimer_enable
.global _native_arm64_htimer_disable
.global _native_arm64_htimer_set_tval
.global _native_arm64_htimer_get_ctl

.section .text

// ulong _native_arm64_timer_get_frequency(void)
// Returns the timer frequency from CNTFRQ_EL0
.balign 4
_native_arm64_timer_get_frequency:
    mrs     x0, cntfrq_el0
    ret

// ulong _native_arm64_get_current_el(void)
// Returns current exception level (0-3) from CurrentEL
.balign 4
_native_arm64_get_current_el:
    mrs     x0, CurrentEL
    lsr     x0, x0, #2
    and     x0, x0, #0x3
    ret

// void _native_arm64_timer_enable_user_access(void)
// Enable EL0 access to physical/virtual counters and timers via CNTKCTL_EL1
.balign 4
_native_arm64_timer_enable_user_access:
    mrs     x0, cntkctl_el1
    orr     x0, x0, #0x1      // EL0PCTEN
    orr     x0, x0, #0x2      // EL0VCTEN
    orr     x0, x0, #0x100    // EL0PTEN
    orr     x0, x0, #0x200    // EL0VTEN
    msr     cntkctl_el1, x0
    isb
    ret

// ulong _native_arm64_timer_get_counter(void)
// Returns the current physical counter value from CNTPCT_EL0
.balign 4
_native_arm64_timer_get_counter:
    mrs     x0, cntpct_el0
    ret

// void _native_arm64_timer_set_compare(ulong value)
// Sets the compare value in CNTP_CVAL_EL0
// x0 = compare value
.balign 4
_native_arm64_timer_set_compare:
    msr     cntp_cval_el0, x0
    isb
    ret

// void _native_arm64_timer_enable(void)
// Enables the physical timer: CNTP_CTL_EL0.ENABLE=1, IMASK=0
.balign 4
_native_arm64_timer_enable:
    mov     x0, #1          // ENABLE=1, IMASK=0
    msr     cntp_ctl_el0, x0
    isb
    ret

// void _native_arm64_timer_disable(void)
// Disables the physical timer: CNTP_CTL_EL0.ENABLE=0
.balign 4
_native_arm64_timer_disable:
    mov     x0, #0
    msr     cntp_ctl_el0, x0
    isb
    ret

// void _native_arm64_timer_set_tval(uint ticks)
// Sets the timer value register CNTP_TVAL_EL0
// The timer will fire when counter reaches (counter + ticks)
// x0 = ticks (32-bit signed value)
.balign 4
_native_arm64_timer_set_tval:
    msr     cntp_tval_el0, x0
    isb
    ret

// uint _native_arm64_timer_get_ctl(void)
// Returns the current timer control register value
.balign 4
_native_arm64_timer_get_ctl:
    mrs     x0, cntp_ctl_el0
    ret

// void _native_arm64_vtimer_enable(void)
// Enables the virtual timer: CNTV_CTL_EL0.ENABLE=1, IMASK=0
.balign 4
_native_arm64_vtimer_enable:
    mov     x0, #1
    msr     cntv_ctl_el0, x0
    isb
    ret

// void _native_arm64_vtimer_disable(void)
// Disables the virtual timer: CNTV_CTL_EL0.ENABLE=0
.balign 4
_native_arm64_vtimer_disable:
    mov     x0, #0
    msr     cntv_ctl_el0, x0
    isb
    ret

// void _native_arm64_vtimer_set_tval(uint ticks)
// Sets CNTV_TVAL_EL0
.balign 4
_native_arm64_vtimer_set_tval:
    msr     cntv_tval_el0, x0
    isb
    ret

// uint _native_arm64_vtimer_get_ctl(void)
// Returns CNTV_CTL_EL0
.balign 4
_native_arm64_vtimer_get_ctl:
    mrs     x0, cntv_ctl_el0
    ret

// void _native_arm64_htimer_enable(void)
// Enables the EL2 physical timer: CNTHP_CTL_EL2.ENABLE=1, IMASK=0
.balign 4
_native_arm64_htimer_enable:
    mov     x0, #1
    msr     cnthp_ctl_el2, x0
    isb
    ret

// void _native_arm64_htimer_disable(void)
// Disables the EL2 physical timer: CNTHP_CTL_EL2.ENABLE=0
.balign 4
_native_arm64_htimer_disable:
    mov     x0, #0
    msr     cnthp_ctl_el2, x0
    isb
    ret

// void _native_arm64_htimer_set_tval(uint ticks)
// Sets CNTHP_TVAL_EL2
.balign 4
_native_arm64_htimer_set_tval:
    msr     cnthp_tval_el2, x0
    isb
    ret

// uint _native_arm64_htimer_get_ctl(void)
// Returns CNTHP_CTL_EL2
.balign 4
_native_arm64_htimer_get_ctl:
    mrs     x0, cnthp_ctl_el2
    ret
