using System;
using System.Runtime;
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

internal static class GCStaticRegionConstants
{
    /// <summary>
    /// Flag set if the corresponding GCStatic entry has not yet been initialized and
    /// the corresponding MethodTable pointer has been changed into a instance pointer of
    /// that MethodTable.
    /// </summary>
    public const int Uninitialized = 0x1;

    /// <summary>
    /// Flag set if the next pointer loc points to GCStaticsPreInitDataNode.
    /// Otherise it is the next GCStatic entry.
    /// </summary>
    public const int HasPreInitializedData = 0x2;

    public const int Mask = Uninitialized | HasPreInitializedData;
}
