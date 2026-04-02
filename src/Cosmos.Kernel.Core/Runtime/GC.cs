
using System.Runtime;
using Cosmos.Kernel.Core.Memory.GarbageCollector;
using Internal.Runtime;
using static Cosmos.Kernel.Core.Memory.GarbageCollector.GarbageCollector;

namespace Cosmos.Kernel.Core.Runtime;

internal static class GC
{

    [RuntimeExport("RhGetGeneration")]
    internal static int RhGetGeneration(object obj)
    {
        //TODO: Implement generational GC.
        return 0;
    }

    [RuntimeExport("RhRegisterForGCReporting")]
    internal static unsafe void RhRegisterForGCReporting(void* pRegistration)
    {

    }

    [RuntimeExport("RhUnregisterForGCReporting")]
    internal static unsafe void RhUnregisterForGCReporting(void* pRegistration)
    {

    }

    [RuntimeExport("RhGetGCDescSize")]
    internal static unsafe int RhGetGCDescSize(MethodTable* pMT)
    {
        if (!pMT->ContainsGCPointers)
        {
            return 0;
        }

        var numSeries = (int)((nint*)pMT)[-1];

        if (numSeries > 0)
        {
            // [GCDescSeriesN, ..., GCDescSeries1, nint numSeries]
            return (int)(IntPtr.Size + numSeries * sizeof(GCDescSeries));
        }
        else
        {
            // [ValSerieItemN, ..., ValSerieItem1, nint offset, nint numSeries]
            return (int)(IntPtr.Size * 2 + (numSeries - 1) * sizeof(ValSerieItem));
        }
    }
}
