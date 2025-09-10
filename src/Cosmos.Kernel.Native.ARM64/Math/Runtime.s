.global ceil
.global g_cpuFeatures
.global RhpAssignRefArm64
.global RhpCheckedAssignRefArm64
.global RhpByRefAssignRefArm64

.text
.align 4

// Math ceiling function for ARM64
// double ceil(double x)
// d0 = input double, returns result in d0
ceil:
    // ARM64 has floating-point rounding instructions
    frintp d0, d0           // Round towards positive infinity (ceiling)
    ret

.bss
.align 8

// CPU features global variable
g_cpuFeatures:
    .space 8                // 64-bit value for CPU features

.text
.align 4

// ARM64 runtime helper for assigning object references
// void RhpAssignRefArm64(Object** dst, Object* src)
// x0 = destination, x1 = source
RhpAssignRefArm64:
    str x1, [x0]           // Store source object reference to destination
    ret

// ARM64 runtime helper for checked assign ref (with write barriers)
// void RhpCheckedAssignRefArm64(Object** dst, Object* src)
// x0 = destination, x1 = source
RhpCheckedAssignRefArm64:
    str x1, [x0]           // Store source object reference to destination
    // TODO: Add proper write barrier for GC if needed
    ret

// ARM64 runtime helper for by-ref assign
// void RhpByRefAssignRefArm64(Object** dst, Object* src)
// x0 = destination, x1 = source
RhpByRefAssignRefArm64:
    str x1, [x0]           // Store source object reference to destination
    ret
