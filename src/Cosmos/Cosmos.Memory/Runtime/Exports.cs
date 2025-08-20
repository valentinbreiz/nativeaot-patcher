// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime;
using System.Runtime.CompilerServices;
using Internal.Runtime;

namespace Cosmos.Memory.Runtime;

public class Exports
{
    [RuntimeExport("RhpNewFast")]
    [MethodImpl(MethodImplOptions.InternalCall)]
    internal static unsafe object* RhpNewFast(MethodTable* pEEType) // BEWARE: not for finalizable objects!
    {
        if (pEEType->IsFinalizable)
        {
            throw new Exception();
        }

        return (object*)Gc.Exports.RhpGcAlloc(pEEType, 0, 0, null);
    }

    [RuntimeExport("RhpNewFinalizable")]
    [MethodImpl(MethodImplOptions.InternalCall)]
    internal static extern unsafe object RhpNewFinalizable(MethodTable* pEEType);

    [RuntimeExport("RhpNewArrayFast")]
    [MethodImpl(MethodImplOptions.InternalCall)]
    internal static extern unsafe object RhpNewArrayFast(MethodTable* pEEType, int length);
}
