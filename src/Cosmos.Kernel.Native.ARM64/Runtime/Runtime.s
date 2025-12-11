// ARM64 NativeAOT Runtime Stubs
// Write barriers, security cookie, knob values accessor, and EH section accessors

.global RhpAssignRefArm64
.global RhpCheckedAssignRefArm64
.global RhpByRefAssignRefArm64
.global RhpAssignRefAVLocation
.global RhpCheckedAssignRefAVLocation
.global RhpByRefAssignRefAVLocation1
.global __security_cookie
.global RhGetKnobValues
.global get_eh_frame_start
.global get_eh_frame_end
.global get_dotnet_eh_table_start
.global get_dotnet_eh_table_end
.global GetModules

.data
.align 8
__security_cookie:
    .quad 0x2B992DDFA23249D6

.text
.align 4

// uint32_t RhGetKnobValues(char*** pResultKeys, char*** pResultValues)
//
// Retrieves compiler-embedded knob values for AppContext initialization
//
// Parameters:
//   x0 - pointer to receive keys array
//   x1 - pointer to receive values array
//
// Returns:
//   w0 - count of knob entries
RhGetKnobValues:
    // g_compilerEmbeddedKnobsBlob layout:
    //   offset 0: m_count (uint32_t)
    //   offset 8: m_first[] (flexible array of pointers)

    adrp    x2, g_compilerEmbeddedKnobsBlob
    add     x2, x2, :lo12:g_compilerEmbeddedKnobsBlob

    // Get count
    ldr     w3, [x2]                // w3 = m_count

    // Calculate keys pointer (m_first starts at offset 8)
    add     x4, x2, #8              // x4 = &m_first[0] (keys)
    str     x4, [x0]                // *pResultKeys = keys

    // Calculate values pointer (m_first + count * 8)
    lsl     x5, x3, #3              // x5 = count * 8
    add     x4, x2, #8
    add     x4, x4, x5              // x4 = &m_first[count] (values)
    str     x4, [x1]                // *pResultValues = values

    mov     w0, w3                  // return count
    ret

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

// void* get_eh_frame_start(void)
// Returns pointer to start of .eh_frame section
get_eh_frame_start:
    adrp    x0, __eh_frame_start
    add     x0, x0, :lo12:__eh_frame_start
    ret

// void* get_eh_frame_end(void)
// Returns pointer to end of .eh_frame section
get_eh_frame_end:
    adrp    x0, __eh_frame_end
    add     x0, x0, :lo12:__eh_frame_end
    ret

// void* get_dotnet_eh_table_start(void)
// Returns pointer to start of .dotnet_eh_table section
get_dotnet_eh_table_start:
    adrp    x0, __dotnet_eh_table_start
    add     x0, x0, :lo12:__dotnet_eh_table_start
    ret

// void* get_dotnet_eh_table_end(void)
// Returns pointer to end of .dotnet_eh_table section
get_dotnet_eh_table_end:
    adrp    x0, __dotnet_eh_table_end
    add     x0, x0, :lo12:__dotnet_eh_table_end
    ret

// size_t GetModules(void** modules)
// Returns the size of the __modules section and populates 'modules' with pointer to start
//
// Parameters:
//   x0 - pointer to receive modules start address
//
// Returns:
//   x0 - size of modules section (end - start)
GetModules:
    adrp    x1, __Modules_start
    add     x1, x1, :lo12:__Modules_start
    str     x1, [x0]                // *modules = __Modules_start

    adrp    x2, __Modules_end
    add     x2, x2, :lo12:__Modules_end
    sub     x0, x2, x1              // return end - start
    ret
