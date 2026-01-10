// ARM64 Generic Timer native functions
// Access to CNTP_* (physical timer) registers

.global _native_arm64_timer_get_frequency
.global _native_arm64_timer_get_counter
.global _native_arm64_timer_set_compare
.global _native_arm64_timer_enable
.global _native_arm64_timer_disable
.global _native_arm64_timer_set_tval
.global _native_arm64_timer_get_ctl

.section .text

// ulong _native_arm64_timer_get_frequency(void)
// Returns the timer frequency from CNTFRQ_EL0
.balign 4
_native_arm64_timer_get_frequency:
    mrs     x0, cntfrq_el0
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
