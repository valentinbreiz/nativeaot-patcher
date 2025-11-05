using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Runtime;

namespace Internal.Runtime
{
    /// <summary>
    /// TypeManagerHandle represents an AOT module in MRT based runtimes.
    /// These handles are a pointer to a TypeManager
    /// </summary>
    public readonly unsafe partial struct TypeManagerHandle(TypeManager* handleValue)
    {
        internal readonly TypeManager* _handleValue = handleValue;

        public TypeManager* AsTypeManager() => _handleValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct TypeManagerSlot
    {
        public TypeManagerHandle TypeManager;
        public int ModuleIndex;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct TypeManager
    {
        public IntPtr OsHandle;
        public ReadyToRunHeader* Header;
        public IntPtr m_pStaticsGCDataSection;
        public IntPtr m_pThreadStaticsDataSection;
        public void** m_pClasslibFunctions;
        public uint m_nClasslibFunctions;

        public TypeManager(IntPtr osModule, ReadyToRunHeader* pHeader, void** pClasslibFunctions, uint nClasslibFunctions)
        {
            int length;
            OsHandle = osModule;
            Header = pHeader;
            m_pClasslibFunctions = pClasslibFunctions;
            m_nClasslibFunctions = nClasslibFunctions;
            m_pStaticsGCDataSection = GetModuleSection(ReadyToRunSectionType.GCStaticRegion, out length);
            m_pThreadStaticsDataSection = GetModuleSection(ReadyToRunSectionType.ThreadStaticRegion, out length);
        }

        public IntPtr GetModuleSection(ReadyToRunSectionType sectionId, out int length)
        {
            ModuleInfoRow* moduleInfoRows, pCurrent;
            IntPtr pResult = IntPtr.Zero;
            length = 0;

            moduleInfoRows = (ModuleInfoRow*)(Header + 1);

            for (int i = 0; i < Header->NumberOfSections; i++)
            {
                pCurrent = moduleInfoRows + i;

                if (sectionId == pCurrent->SectionId)
                {
                    length = pCurrent->GetLength();
                    pResult = pCurrent->Start;
                    break;
                }
            }

            return pResult;
        }

        public void* GetClassLibFunction(ClassLibFunctionId functionId)
        {
            uint id = (uint)functionId;

            if (id >= m_nClasslibFunctions)
            {
                return (void*)IntPtr.Zero;
            }

            return m_pClasslibFunctions[id];
        }
    }

    public enum ClassLibFunctionId
    {
        GetRuntimeException = 0,
        FailFast = 1,
        ThreadEntryPoint = 2,
        AppendExceptionStackFrame = 3,
        // unused = 4,
        GetSystemArrayEEType = 5,
        OnFirstChance = 6,
        OnUnhandledException = 7,
        ObjectiveCMarshalTryGetTaggedMemory = 8,
        ObjectiveCMarshalGetIsTrackedReferenceCallback = 9,
        ObjectiveCMarshalGetOnEnteredFinalizerQueueCallback = 10,
        ObjectiveCMarshalGetUnhandledExceptionPropagationHandler = 11,
    }
}
