using System;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using Cosmos.Kernel.Core.Memory;
using Internal.Runtime;

namespace Cosmos.Kernel.Core.Runtime;

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

                if (section.SectionId == ReadyToRunSectionType.DehydratedData)
                {
                    // TODO: Either bring more types from coreclr so we can rehydrate the data
                    //       or wait till .NET 10 and use UnsafeAccessorAttribute (https://github.com/dotnet/runtime/issues/90081).
                }

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
        if (!MethodTable.SupportsRelativePointers)
            throw new InvalidProgramException("The compiled binary does not use relative pointers.");

        int currentBase = 0;
        for (byte* block = (byte*)rgnStart; (nint)block < rgnEnd; block += sizeof(int))
        {
            nint* pBlock = (IntPtr*)ReadRelPtr32(block);
            nint blockAddr = (nint)ReadRelPtr32(pBlock);

            if ((blockAddr & GCStaticRegionConstants.Uninitialized) == GCStaticRegionConstants.Uninitialized)
            {
                var pMT = (MethodTable*)(blockAddr & ~GCStaticRegionConstants.Mask);
                var ptr = Memory.RhpNewFast(pMT);
                object obj = Unsafe.AsRef<object>(ptr);

                if ((blockAddr & GCStaticRegionConstants.HasPreInitializedData) == GCStaticRegionConstants.HasPreInitializedData)
                {
                    void* pPreInitDataAddr = ReadRelPtr32((int*)pBlock + 1);
                    var pEEType = Memory.GetMethodTable(obj);
                    var size = pEEType->RawBaseSize - (uint)sizeof(ObjHeader) - (uint)sizeof(MethodTable*);
                    byte* objPtr = (byte*)Unsafe.AsPointer(ref obj);
                    byte* destPtr = objPtr + sizeof(MethodTable*);
                    MemoryOp.MemMove(destPtr, (byte*)pPreInitDataAddr, (int)size);
                }

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
