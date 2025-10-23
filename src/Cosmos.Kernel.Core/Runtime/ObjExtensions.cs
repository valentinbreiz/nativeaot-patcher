using System.Runtime.CompilerServices;
using Internal.Runtime;

namespace Cosmos.Kernel.Core.Runtime;

internal static class ObjExtensions
{
    internal static unsafe MethodTable* GetMethodTable(this object obj)
    {
        // The MethodTable pointer is stored at the beginning of every object
        // RhpNewFast sets: *result = pMT; where result is the allocated address
        return *(MethodTable**)Unsafe.AsPointer(ref obj);
    }
}
