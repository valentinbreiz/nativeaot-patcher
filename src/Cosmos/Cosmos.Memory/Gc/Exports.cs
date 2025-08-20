// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime;
using Internal.Runtime;

namespace Cosmos.Memory.Gc;

public static unsafe class Exports
{
    /// <summary>
    ///
    /// </summary>
    /// <param name="pEEType">type of the object</param>
    /// <param name="uFlags">GC type flags (see gc.h GC_ALLOC_*)</param>
    /// <param name="numElements">number of array elements</param>
    /// <param name="pTransitionFrame">transition frame to make stack crawlable</param>
    /// <returns> Returns a pointer to the object allocated or NULL on failure.</returns>
    [RuntimeExport("RhpGcAlloc")]
    internal static void* RhpGcAlloc(MethodTable* pEEType, UInt32 uFlags, UIntPtr numElements, void* pTransitionFrame)
    {

        return GcManger.AllocInternal(pEEType, uFlags, (uint)numElements);
    }

    [RuntimeExport("RhpNewFinalizable")]
    internal static object* RhpNewFinalizable(MethodTable* pEEType)
    {
        if (!pEEType->IsFinalizable)
        {
            throw new InvalidOperationException();
        }
        return GcManger.AllocInternal(pEEType, GC_ALLOC_FLAGS.GC_ALLOC_FINALIZE, 0);
    }

}
