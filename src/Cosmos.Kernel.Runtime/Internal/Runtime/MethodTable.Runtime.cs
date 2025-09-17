using System;
using System.Runtime;
using System.Runtime.CompilerServices;
using Cosmos.Kernel.Runtime;

namespace Internal.Runtime;


internal unsafe partial struct MethodTable
{
    internal MethodTable* GetArrayEEType()
    {
        void* pGetArrayEEType = ModuleHelpers.RhpGetClasslibFunctionFromEEType((MethodTable*)Unsafe.AsPointer(ref this), ClassLibFunctionId.GetSystemArrayEEType);
        return ((delegate*<MethodTable*>)pGetArrayEEType)();
        return MethodTable.Of<Array>();
    }
    /*
    internal Exception GetClasslibException(ExceptionIDs id)
    {
        if (IsParameterizedType)
        {
            return RelatedParameterType->GetClasslibException(id);
        }

        return EH.GetClasslibExceptionFromEEType(id, (MethodTable*)Unsafe.AsPointer(ref this));
    }
    */
    internal IntPtr GetClasslibFunction(ClassLibFunctionId id)
    {
        return (IntPtr)ModuleHelpers.RhpGetClasslibFunctionFromEEType((MethodTable*)Unsafe.AsPointer(ref this), id);
    }
}
