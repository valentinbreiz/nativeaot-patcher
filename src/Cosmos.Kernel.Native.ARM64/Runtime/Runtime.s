.global RhpAssignRefArm64
.global RhpCheckedAssignRefArm64

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
