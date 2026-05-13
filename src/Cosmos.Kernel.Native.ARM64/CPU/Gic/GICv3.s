// ARM64 GICv3 System Register Access
// GICv3 CPU interface uses ICC_* system registers instead of MMIO

.global _native_arm64_gicv3_read_icc_iar1_el1
.global _native_arm64_gicv3_write_icc_eoir1_el1
.global _native_arm64_gicv3_read_icc_hppir1_el1
.global _native_arm64_gicv3_write_icc_pmr_el1
.global _native_arm64_gicv3_read_icc_pmr_el1
.global _native_arm64_gicv3_write_icc_bpr1_el1
.global _native_arm64_gicv3_write_icc_ctlr_el1
.global _native_arm64_gicv3_read_icc_ctlr_el1
.global _native_arm64_gicv3_write_icc_igrpen1_el1
.global _native_arm64_gicv3_read_icc_igrpen1_el1
.global _native_arm64_gicv3_write_icc_sgi1r_el1
.global _native_arm64_gicv3_read_icc_sre_el1
.global _native_arm64_gicv3_write_icc_sre_el1
.global _native_arm64_read_mpidr_el1

.section .text

// uint _native_arm64_gicv3_read_icc_iar1_el1(void)
// Acknowledge Group 1 interrupt - returns interrupt ID
.balign 4
_native_arm64_gicv3_read_icc_iar1_el1:
    mrs     x0, icc_iar1_el1
    ret

// void _native_arm64_gicv3_write_icc_eoir1_el1(uint intId)
// End of Interrupt for Group 1
.balign 4
_native_arm64_gicv3_write_icc_eoir1_el1:
    msr     icc_eoir1_el1, x0
    isb
    ret

// uint _native_arm64_gicv3_read_icc_hppir1_el1(void)
// Read Highest Priority Pending Interrupt
.balign 4
_native_arm64_gicv3_read_icc_hppir1_el1:
    mrs     x0, icc_hppir1_el1
    ret

// void _native_arm64_gicv3_write_icc_pmr_el1(uint priority)
// Set Priority Mask Register
.balign 4
_native_arm64_gicv3_write_icc_pmr_el1:
    msr     icc_pmr_el1, x0
    isb
    ret

// uint _native_arm64_gicv3_read_icc_pmr_el1(void)
// Read Priority Mask Register
.balign 4
_native_arm64_gicv3_read_icc_pmr_el1:
    mrs     x0, icc_pmr_el1
    ret

// void _native_arm64_gicv3_write_icc_bpr1_el1(uint value)
// Set Binary Point Register for Group 1
.balign 4
_native_arm64_gicv3_write_icc_bpr1_el1:
    msr     icc_bpr1_el1, x0
    isb
    ret

// void _native_arm64_gicv3_write_icc_ctlr_el1(uint value)
// Set CPU Interface Control Register
.balign 4
_native_arm64_gicv3_write_icc_ctlr_el1:
    msr     icc_ctlr_el1, x0
    isb
    ret

// uint _native_arm64_gicv3_read_icc_ctlr_el1(void)
// Read CPU Interface Control Register
.balign 4
_native_arm64_gicv3_read_icc_ctlr_el1:
    mrs     x0, icc_ctlr_el1
    ret

// void _native_arm64_gicv3_write_icc_igrpen1_el1(uint value)
// Enable/disable Group 1 interrupts
.balign 4
_native_arm64_gicv3_write_icc_igrpen1_el1:
    msr     icc_igrpen1_el1, x0
    isb
    ret

// uint _native_arm64_gicv3_read_icc_igrpen1_el1(void)
// Read Group 1 interrupt enable state
.balign 4
_native_arm64_gicv3_read_icc_igrpen1_el1:
    mrs     x0, icc_igrpen1_el1
    ret

// void _native_arm64_gicv3_write_icc_sgi1r_el1(ulong value)
// Generate Software Generated Interrupt (SGI) for Group 1
.balign 4
_native_arm64_gicv3_write_icc_sgi1r_el1:
    msr     icc_sgi1r_el1, x0
    isb
    ret

// uint _native_arm64_gicv3_read_icc_sre_el1(void)
// Read System Register Enable - used to detect GICv3
.balign 4
_native_arm64_gicv3_read_icc_sre_el1:
    mrs     x0, icc_sre_el1
    ret

// void _native_arm64_gicv3_write_icc_sre_el1(uint value)
// Write System Register Enable - must enable SRE before using ICC_* registers
// on real hardware. Bit 0 (SRE) must be set to 1.
.balign 4
_native_arm64_gicv3_write_icc_sre_el1:
    msr     icc_sre_el1, x0
    isb
    ret

// ulong _native_arm64_read_mpidr_el1(void)
// Read MPIDR_EL1 - Multiprocessor Affinity Register
// Used to determine current CPU's affinity for redistributor matching
// Returns: Aff3[39:32] | Aff2[23:16] | Aff1[15:8] | Aff0[7:0]
.balign 4
_native_arm64_read_mpidr_el1:
    mrs     x0, mpidr_el1
    ret
