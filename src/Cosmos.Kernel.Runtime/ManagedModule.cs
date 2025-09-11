using System;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.Memory;
using Internal.Runtime;

namespace Cosmos.Kernel.Runtime;

#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

/// <summary>
/// Handles the initialization of managed modules.
/// </summary>
// https://github.com/dotnet/runtime/blob/main/docs/design/coreclr/botr/readytorun-format.md
public static unsafe class ManagedModule
{

    static void* ReadRelPtr32(void* address)
    {
        if (address == null)
            throw new InvalidProgramException("A null dereference was attempted while reading a relative pointer.");

        return (byte*)address + *(int*)address;
    }

    /// <summary>
    /// Initializes all given managed modules present in a compiled binary.
    /// </summary>
    /// <param name="modules">The modules to initialize.</param>
    /// <exception cref="InvalidProgramException">Thrown when the compiled binary has malformed runtime information.</exception>
    public static void InitializeAll(Span<nint> modules)
    {
        for (var i = 0; i < modules.Length; i++)
        {
            if (modules[i] == 0)
                break;

            var header = (ReadyToRunHeader*)modules[i];
            var sections = (ModuleInfoRow*)(header + 1);

            if (header == null)
                throw new InvalidProgramException("An entry in the managed module section points to null.");

            if (header->Signature != ReadyToRunHeaderConstants.Signature)
                throw new InvalidProgramException("Unexpected signature found in a R2R module.");

            for (int j = 0; j < header->NumberOfSections; j++)
            {
                ref var section = ref sections[j];
                if (section.SectionId == ReadyToRunSectionType.GCStaticRegion)
                {
                    if (section.Start == 0 || section.End == 0)
                        throw new InvalidProgramException("A GC static region section has a null start or end.");

                    InitializeStatics(section.Start, section.End);
                }

                if (section.SectionId == ReadyToRunSectionType.EagerCctor)
                {
                    if (section.Start == 0 || section.End == 0)
                        throw new InvalidProgramException("A eager constructor section has a null start or end.");

                    RunFuncRelPtrs(section.Start, section.End);
                }

                if (section.SectionId == ReadyToRunSectionType.ModuleInitializerList)
                {
                    if (section.Start == 0 || section.End == 0)
                        throw new InvalidProgramException("A module initialization section has a null start or end.");

                    RunFuncRelPtrs(section.Start, section.End);
                }
            }
        }
    }

    // Runs an array of relative pointers to functions that do not return any value
    // and accept no parameters. The length of the array is determined via "rgnEnd - rgnStart".
    static void RunFuncRelPtrs(nint rgnStart, nint rgnEnd)
    {
        byte* funcs = (byte*)rgnStart;

        for (byte* curr = funcs; curr < (byte*)rgnEnd; curr += sizeof(int))
        {
            ((delegate*<void>)ReadRelPtr32(curr))();
        }
    }

    static void InitializeStatics(nint rgnStart, nint rgnEnd)
    {
        int currentBase = 0;
        for (byte* block = (byte*)rgnStart; (nint)block < rgnEnd; block += sizeof(int))
        {
            // GC Static regions can be shared by modules linked together during compilation. To ensure each
            // is initialized once, the static region pointer is stored with lowest bit set in the image.
            // The first time we initialize the static region its pointer is replaced with an object reference
            // whose lowest bit is no longer set.
            nint* pBlock = (IntPtr*)ReadRelPtr32(block);
            nint blockAddr = (nint)ReadRelPtr32(pBlock);

            if ((blockAddr & GCStaticRegionConstants.Uninitialized) == GCStaticRegionConstants.Uninitialized)
            {
                var ptr = StartupCodeHelpers.RhpNewFast((MethodTable*)(blockAddr & ~GCStaticRegionConstants.Mask));
                object obj = Unsafe.AsRef<object>(ptr);
                var pEEType = StartupCodeHelpers.GetMethodTable(obj);

                if ((blockAddr & GCStaticRegionConstants.HasPreInitializedData) == GCStaticRegionConstants.HasPreInitializedData)
                {
                    // The next pointer is preinitialized data blob that contains preinitialized static GC fields,
                    // which are pointer relocs to GC objects in frozen segment.
                    // It actually has all GC fields including non-preinitialized fields and we simply copy over the
                    // entire blob to this object, overwriting everything.
                    void* pPreInitDataAddr = ReadRelPtr32((int*)pBlock + 1);
                    var size = pEEType->_uBaseSize - (uint)sizeof(ObjHeader) - (uint)sizeof(MethodTable*);

                    MemoryOp.MemMove((byte*)Unsafe.AsPointer(ref ((RawData)obj).Data), (byte*)pPreInitDataAddr, (int)size);
                }

                // Update the base pointer to point to the pinned object
                *pBlock = *(IntPtr*)&obj;
            }

            currentBase++;
        }
    }
}


[StructLayout(LayoutKind.Sequential)]
internal class RawData
{
    public byte Data;
}
[StructLayout(LayoutKind.Sequential)]
internal struct ObjHeader
{
    // Contents of the object header
    private IntPtr _objHeaderContents;
}
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ModuleInfoRow
{
    internal ReadyToRunSectionType SectionId;
    internal int Flags;
    internal nint Start;
    internal nint End;
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
