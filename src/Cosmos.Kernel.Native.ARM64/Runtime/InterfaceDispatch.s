// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Interface dispatch stubs for Cosmos OS (ARM64)

.text
.align 4

// External C function
.extern RhpCidResolve

// Initial dispatch on an interface when we don't have a cache yet.
// This is the entry point called from interface dispatch sites before
// the dispatch cell has been resolved.
//
// On entry (AAPCS64):
//   x0 = 'this' pointer (the object we're dispatching on)
//   x1-x7 = parameters to the interface method
//   x11 = pointer to the interface dispatch cell
//
// The dispatch cell structure is:
//   Cell[0].m_pStub  = pointer to this function (RhpInitialDynamicInterfaceDispatch)
//   Cell[0].m_pCache = interface type pointer | flags
//   Cell[1].m_pStub  = 0
//   Cell[1].m_pCache = interface slot number
//
.global RhpInitialDynamicInterfaceDispatch
.type RhpInitialDynamicInterfaceDispatch, %function

RhpInitialDynamicInterfaceDispatch:
    // Trigger an exception if we're dispatching on a null this
    // Load first byte to trigger fault if null
    ldrb    wzr, [x0]

    // Save registers that will be clobbered
    // Interface method parameters: x0='this', x1-x7=params
    // Save all argument registers and link register
    stp     x0, x1, [sp, #-80]!     // Save x0, x1 and allocate 80 bytes
    stp     x2, x3, [sp, #16]       // Save x2, x3
    stp     x4, x5, [sp, #32]       // Save x4, x5
    stp     x6, x7, [sp, #48]       // Save x6, x7
    str     x30, [sp, #64]          // Save link register (LR)

    // Set up parameters for RhpCidResolve (AAPCS64):
    //   x0 = 'this' pointer (already in x0, still there)
    //   x1 = dispatch cell pointer
    mov     x1, x11

    // Call RhpCidResolve to resolve the interface method
    // It returns the target method address in x0
    bl      RhpCidResolve

    // Save the resolved method address
    mov     x16, x0

    // Restore the original parameters for the interface method
    ldr     x30, [sp, #64]          // Restore link register
    ldp     x6, x7, [sp, #48]       // Restore x6, x7
    ldp     x4, x5, [sp, #32]       // Restore x4, x5
    ldp     x2, x3, [sp, #16]       // Restore x2, x3
    ldp     x0, x1, [sp], #80       // Restore x0, x1 and deallocate stack

    // Jump to the resolved method address (in x16)
    br      x16

.size RhpInitialDynamicInterfaceDispatch, . - RhpInitialDynamicInterfaceDispatch
