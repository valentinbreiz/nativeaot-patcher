using System.Runtime.CompilerServices;
using Internal.Runtime;

namespace Cosmos.Kernel.Core.Runtime;

internal static class ObjExtensions
{
    internal static unsafe MethodTable* GetMethodTable(this object obj)
    {
        // Unsafe.AsPointer(ref obj) gives the stack address of the local 'obj' variable,
        // which contains the managed reference (= object address).
        // We need TWO dereferences:
        //   1. *(void**)Unsafe.AsPointer(ref obj)  → object address (the managed reference value)
        //   2. *(MethodTable**)(object address)     → MethodTable* at offset 0 of the object
        void* objAddr = *(void**)Unsafe.AsPointer(ref obj);
        return *(MethodTable**)objAddr;
    }
}
