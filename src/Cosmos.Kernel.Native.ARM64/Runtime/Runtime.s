// ARM64 NativeAOT Write Barriers
// Standard implementation for object reference assignments

.global RhpAssignRefArm64
.global RhpCheckedAssignRefArm64
.global RhpByRefAssignRefArm64
.global RhpAssignRefAVLocation
.global RhpCheckedAssignRefAVLocation
.global RhpByRefAssignRefAVLocation1

.text
.align 4

// void RhpByRefAssignRefArm64()
//
// Write barrier for by-reference assignments (copies object reference from one location to another)
//
// On entry:
//   x13 : source address (points to object reference to copy)
//   x14 : destination address (where to write the reference)
//
// On exit:
//   x13 : incremented by 8
//   x14 : incremented by 8
//   x15 : trashed
RhpByRefAssignRefArm64:
RhpByRefAssignRefAVLocation1:
        ldr     x15, [x13], #8      // Load source object reference, post-increment x13
        b       RhpCheckedAssignRefArm64

// void RhpCheckedAssignRefArm64()
//
// Write barrier for assignments to locations that may not be on the managed heap
// (e.g., static fields, stack locations)
//
// On entry:
//   x14 : destination address
//   x15 : object reference to store
//
// On exit:
//   x14 : incremented by 8
RhpCheckedAssignRefArm64:
RhpCheckedAssignRefAVLocation:
        // Store the object reference
        str     x15, [x14], #8      // Store and post-increment x14
        ret

// void RhpAssignRefArm64()
//
// Write barrier for assignments to the managed heap
// Ensures proper memory ordering for concurrent GC
//
// On entry:
//   x14 : destination address (on managed heap)
//   x15 : object reference to store
//
// On exit:
//   x14 : incremented by 8
RhpAssignRefArm64:
RhpAssignRefAVLocation:
        // Store the object reference
        str     x15, [x14], #8      // Store and post-increment x14

        // Memory barrier to ensure stores are visible to other cores and GC
        dmb     ish                 // Inner shareable domain barrier (sufficient for ARM64)

        ret
