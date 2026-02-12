using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.Runtime;

namespace Internal.Runtime;

internal unsafe partial struct MethodTable
{
    internal MethodTable* GetArrayEEType()
    {
        void* pGetArrayEEType = ModuleHelpers.RhpGetClasslibFunctionFromEEType((MethodTable*)Unsafe.AsPointer(ref this), ClassLibFunctionId.GetSystemArrayEEType);
        return ((delegate*<MethodTable*>)pGetArrayEEType)();
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

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ModuleInfoRow
{
    internal ReadyToRunSectionType SectionId;
    internal int Flags;
    internal nint Start;
    internal nint End;
    internal readonly int GetLength() => (int)(End - Start);
};
