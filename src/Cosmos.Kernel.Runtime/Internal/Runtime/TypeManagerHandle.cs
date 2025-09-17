using System;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Runtime;

namespace Internal.Runtime
{
    /// <summary>
    /// TypeManagerHandle represents an AOT module in MRT based runtimes.
    /// These handles are a pointer to a TypeManager
    /// </summary>
    internal unsafe partial struct TypeManagerHandle
    {
        private TypeManager* _handleValue;

        public TypeManagerHandle(TypeManager* handleValue)
        {
            _handleValue = handleValue;
        }

        public readonly TypeManager* AsTypeManager() => _handleValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct TypeManager
    {
        public IntPtr OsHandle;
        public ReadyToRunHeader* Header;
        public byte* m_pStaticsGCDataSection;
        public byte* m_pThreadStaticsDataSection;
        public void** m_pClasslibFunctions;
        public uint m_nClasslibFunctions;

        public TypeManager(IntPtr osModule, ReadyToRunHeader* pHeader, void** pClasslibFunctions, uint nClasslibFunctions)
        {
            OsHandle = osModule;
            Header = pHeader;
            m_pClasslibFunctions = pClasslibFunctions;
            m_nClasslibFunctions = nClasslibFunctions;
            m_pStaticsGCDataSection = (byte*)GetModuleSection(ReadyToRunSectionType.GCStaticRegion, out _);
            m_pThreadStaticsDataSection = (byte*)GetModuleSection(ReadyToRunSectionType.ThreadStaticRegion, out _);
        }

        public IntPtr GetModuleSection(ReadyToRunSectionType sectionId, out int length)
        {
            ModuleInfoRow* moduleInfoRows = (ModuleInfoRow*)(Header + 1);

            for (int i = 0; i < Header->NumberOfSections; i++)
            {
                ModuleInfoRow* pCurrent = moduleInfoRows + i;
                if (sectionId == pCurrent->SectionId)
                {
                    length = pCurrent->GetLength();
                    return pCurrent->Start;
                }
            }

            length = 0;
            return IntPtr.Zero;
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
    
    internal enum ClassLibFunctionId
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
