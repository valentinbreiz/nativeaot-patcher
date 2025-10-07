using System;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.System.IO;
using Internal.Runtime;

namespace Cosmos.Kernel.Core.Runtime;

#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

/// <summary>
/// Handles the initialization of managed modules.
/// </summary>
// https://github.com/dotnet/runtime/blob/main/docs/design/coreclr/botr/readytorun-format.md
public static unsafe partial class ManagedModule
{
    /// <summary>
    /// Table of logical modules.
    /// </summary>
    private static TypeManagerHandle[] s_modules;
    private static int s_moduleCount = 0;

    [LibraryImport("*", EntryPoint = "GetModules")]
    internal static unsafe partial uint GetModules(out ReadyToRunHeader* modules);

    //This method requires no optimization and inlining to ensure the stack is not corrupted.
    [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
    public static void InitializeModules()
    {
        Serial.WriteString("[ManagedModule] - Initilizing Module Handlers - Starting\n");
        var count = GetModules(out var modulesptr);
        Serial.WriteString("[ManagedModule] - Found ");
        Serial.WriteNumber(count);
        Serial.WriteString(" modules\n");
        // TODO: We need the classlib functions, implement the array.
        var modules = ModuleHelpers.CreateTypeManagers((nint)modulesptr, new(modulesptr, (int)count), (void**)IntPtr.Zero, 0);

        for (int i = 0; i < modules.Length; i++)
        {
            Serial.WriteString("[ManagedModule] - Setting TypeManagerSlot for module ");
            Serial.WriteNumber(i);
            Serial.WriteString("\n");

            InitializeGlobalTablesForModule(modules[i], i);

            //Serial.WriteString("[ManagedModule] - Running Static Constructors for all modules\n");        
            RunInitializers(modules[i], ReadyToRunSectionType.EagerCctor);
        }

        s_modules = modules;
        s_moduleCount = modules.Length;
        Serial.WriteString("[ManagedModule] - Initilizing Module Handlers - Complete\n");
    }

    private static void InitializeGlobalTablesForModule(TypeManagerHandle typeManagerHandle, int moduleIndex)
    {
        int length;
        TypeManagerSlot* section;

        TypeManager* typeManager = typeManagerHandle.AsTypeManager();

        section = (TypeManagerSlot*)typeManager->GetModuleSection(ReadyToRunSectionType.TypeManagerIndirection, out length);
        section->TypeManager = typeManagerHandle;
        section->ModuleIndex = moduleIndex;

        IntPtr gcStaticBase = typeManager->GetModuleSection(ReadyToRunSectionType.GCStaticRegion, out length);
        if (gcStaticBase != IntPtr.Zero)
        {
            Serial.WriteString("[ManagedModule] - Initializing Statics for module ");
            Serial.WriteNumber(moduleIndex);
            Serial.WriteString("\n");

            InitializeStatics(gcStaticBase, length);
        }
    }

    public static void RunModuleInitializers()
    {
        for (int i = 0; i < s_moduleCount; i++)
        {
            Serial.WriteString("[ManagedModule] - Running Module Initializers for module ");
            Serial.WriteNumber(i);
            Serial.WriteString("\n");

            RunInitializers(s_modules[i], ReadyToRunSectionType.ModuleInitializerList);
        }
    }

    private static unsafe void RunInitializers(TypeManagerHandle typeManager, ReadyToRunSectionType section)
    {
        var pInitializers = (byte*)typeManager.AsTypeManager()->GetModuleSection(section, out int length);

        Serial.WriteString("[ManagedModule] - Running Initializers, found ");
        Serial.WriteNumber(length / (MethodTable.SupportsRelativePointers ? sizeof(int) : sizeof(nint)));
        Serial.WriteString(" initializers for section ");
        Serial.WriteNumber((int)section);
        Serial.WriteString("\n");

        for (byte* pCurrent = pInitializers;
            pCurrent < (pInitializers + length);
            pCurrent += MethodTable.SupportsRelativePointers ? sizeof(int) : sizeof(nint))
        {
            Serial.WriteString("[ManagedModule] - Running Initializer at address ");
            Serial.WriteHex((uint)(nint)pCurrent);
            Serial.WriteString("\n");
            var initializer = MethodTable.SupportsRelativePointers ? (delegate*<void>)ReadRelPtr32(pCurrent) : *(delegate*<void>*)pCurrent;
            initializer();
            Serial.WriteString("[ManagedModule] - Completed Initializer at address ");
            Serial.WriteHex((uint)(nint)pCurrent);
            Serial.WriteString("\n");
        }

        static void* ReadRelPtr32(void* address)
            => (byte*)address + *(int*)address;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void InitializeStatics(IntPtr gcStaticRegionStart, int length)
    {
        byte* gcStaticRegionEnd = ((byte*)gcStaticRegionStart) + length;

        int currentBase = 0;
        for (byte* block = (byte*)gcStaticRegionStart;
            block < gcStaticRegionEnd;
            block += MethodTable.SupportsRelativePointers ? sizeof(int) : sizeof(nint))
        {

            IntPtr* pBlock = MethodTable.SupportsRelativePointers ? (IntPtr*)ReadRelPtr32(block) : *(IntPtr**)block;
            nint blockAddr = MethodTable.SupportsRelativePointers ? (nint)ReadRelPtr32(pBlock) : *pBlock;

            if ((blockAddr & GCStaticRegionConstants.Uninitialized) == GCStaticRegionConstants.Uninitialized)
            {
                object? obj = null;
                var pMT = (MethodTable*)(blockAddr & ~GCStaticRegionConstants.Mask);
                Memory.RhAllocateNewObject(pMT, 0, &obj);

                if (obj is null)
                {
                    Serial.WriteString("Failed allocating GC static bases\n");
                    throw new OutOfMemoryException();
                }

                if ((blockAddr & GCStaticRegionConstants.HasPreInitializedData) == GCStaticRegionConstants.HasPreInitializedData)
                {
                    void* pPreInitDataAddr = MethodTable.SupportsRelativePointers ? ReadRelPtr32((int*)pBlock + 1) : (void*)*(pBlock + 1);
                    var size = pMT->RawBaseSize - (uint)sizeof(ObjHeader) - (uint)sizeof(MethodTable*);
                    byte* destPtr = (byte*)&obj + sizeof(MethodTable*);
                    MemoryOp.MemMove(destPtr, (byte*)pPreInitDataAddr, (int)size);
                }

                *pBlock = *(IntPtr*)&obj;
            }

            currentBase++;
        }

        static void* ReadRelPtr32(void* address)
            => (byte*)address + *(int*)address;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal class RawData
{
    public byte Data;
}
