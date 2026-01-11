// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime;
using System.Runtime.CompilerServices;
using Cosmos.Kernel.Core.IO;
using Internal.Runtime;

namespace Cosmos.Kernel.Core.Runtime;

internal static class CachedInterfaceDispatch
{
    [RuntimeExport("RhResolveDispatch")]
    private static unsafe IntPtr RhResolveDispatch(object pObject, MethodTable* interfaceType, ushort slot)
    {
        Serial.WriteString("Result Call");
        var result = RhpResolveDispatch(null!, pObject, (object)(nint)interfaceType, slot);
        Serial.WriteString("Result got");
        return result;
    }

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "RhResolveDispatch")]
    private static extern unsafe IntPtr RhpResolveDispatch([UnsafeAccessorType("System.Runtime.CachedInterfaceDispatch")]object CachedInterfaceDispatch, object pObject, [UnsafeAccessorType("Internal.Runtime.MethodTable*")]object interfaceType, ushort slot);
}
