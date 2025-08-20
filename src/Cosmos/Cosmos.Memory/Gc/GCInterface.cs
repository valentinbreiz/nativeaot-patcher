// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Memory.Gc;

// this needs to be kept up to date with src/coreclr/gc/gcinterface.h


public static class GC_ALLOC_FLAGS
{
    public const UInt32 GC_ALLOC_NO_FLAGS = 0;
    public const UInt32 GC_ALLOC_FINALIZE = 1;
    public const UInt32 GC_ALLOC_CONTAINS_REF = 2;
    public const UInt32 GC_ALLOC_ALIGN8_BIAS = 4;
    public const UInt32 GC_ALLOC_ALIGN8 = 8;
    // Only implies the initial allocation is 8 byte aligned.
    // Preserving the alignment across relocation depends on
    // RESPECT_LARGE_ALIGNMENT also being defined.

    public const UInt32 GC_ALLOC_ZEROING_OPTIONAL = 16;
    public const UInt32 GC_ALLOC_LARGE_OBJECT_HEAP = 32;
    public const UInt32 GC_ALLOC_PINNED_OBJECT_HEAP = 64;
    public const UInt32 GC_ALLOC_USER_OLD_HEAP = GC_ALLOC_LARGE_OBJECT_HEAP | GC_ALLOC_PINNED_OBJECT_HEAP;
}
