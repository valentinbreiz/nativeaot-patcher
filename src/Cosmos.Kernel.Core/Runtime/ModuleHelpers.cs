using System.Runtime;
using System.Runtime.CompilerServices;
using Cosmos.Kernel.System.IO;
using Internal.Runtime;

namespace Cosmos.Kernel.Core.Runtime;

internal static unsafe class ModuleHelpers
{
    private static void* _osmodule;
    [RuntimeExport("RhpGetModuleSection")]
    internal static void* RhpGetModuleSection(TypeManagerHandle* module, ReadyToRunSectionType sectionId, int* length)
    {
        nint section = module->AsTypeManager()->GetModuleSection(sectionId, out int len);
        length = &len;
        return (void*)section;
    }

    [RuntimeExport("RhpRegisterOsModule")]
    internal static void* RhpRegisterOsModule(void* osModule)
    {
        //TODO: Should be saved on an array or some other struct.
        _osmodule = osModule;
        return osModule;
    }

    [RuntimeExport("RhpCreateTypeManager")]
    internal static unsafe TypeManagerHandle RhpCreateTypeManager(IntPtr osModule, ReadyToRunHeader* moduleHeader, void** pClasslibFunctions, uint nClasslibFunctions)
    {
        TypeManager* tm = (TypeManager*)Memory.RhpNewFast(MethodTable.Of<TypeManager>());
        tm->OsHandle = osModule;
        tm->Header = moduleHeader;
        tm->m_pClasslibFunctions = pClasslibFunctions;
        tm->m_nClasslibFunctions = nClasslibFunctions;
        tm->m_pStaticsGCDataSection = tm->GetModuleSection(ReadyToRunSectionType.GCStaticRegion, out _);
        tm->m_pThreadStaticsDataSection = tm->GetModuleSection(ReadyToRunSectionType.ThreadStaticRegion, out _);

        return new TypeManagerHandle(tm);
        // TypeManager typeManager = new(osModule, moduleHeader, pClasslibFunctions, nClasslibFunctions);
        // return new TypeManagerHandle((TypeManager*)Unsafe.AsPointer(ref typeManager));
    }
    [RuntimeExport("RhpGetClasslibFunctionFromCodeAddress")]
    internal static unsafe void* RhpGetClasslibFunctionFromCodeAddress(IntPtr address, ClassLibFunctionId id)
    {
        //Requires some work;
        return (void*)IntPtr.Zero;
    }

    [RuntimeExport("RhpGetClasslibFunctionFromEEType")]
    internal static unsafe void* RhpGetClasslibFunctionFromEEType(MethodTable* pEEType, ClassLibFunctionId id)
    {
        return pEEType->TypeManager.AsTypeManager()->GetClassLibFunction(id);
    }


    internal static unsafe TypeManagerHandle[] CreateTypeManagers(IntPtr osModule, Span<nint> pModuleHeaders, void** pClasslibFunctions, uint nClasslibFunctions)
    {
        // Count the number of modules so we can allocate an array to hold the TypeManager objects.
        // At this stage of startup, complex collection classes will not work.
        int moduleCount = 0;
        for (int i = 0; i < pModuleHeaders.Length; i++)
        {
            // The null pointers are sentinel values and padding inserted as side-effect of
            // the section merging. (The global static constructors section used by C++ has
            // them too.)
            if (pModuleHeaders[i] != IntPtr.Zero)
                moduleCount++;
        }

        // We cannot use the new keyword just yet, so stackalloc the array first
        var pHandles = stackalloc TypeManagerHandle[moduleCount];
        int moduleIndex = 0;
        for (int i = 0; i < pModuleHeaders.Length; i++)
        {
            if (pModuleHeaders[i] != IntPtr.Zero)
            {
                TypeManagerHandle handle = RhpCreateTypeManager(pModuleHeaders[0], (ReadyToRunHeader*)pModuleHeaders[i], pClasslibFunctions, nClasslibFunctions);

                IntPtr dehydratedRegion = handle.AsTypeManager()->GetModuleSection(ReadyToRunSectionType.DehydratedData, out int length);
                if (dehydratedRegion != IntPtr.Zero)
                {
                    Serial.WriteString("[ManagedModule] - Dehydrated Data found for module ");
                    Serial.WriteNumber(moduleIndex);
                    Serial.WriteString("\n");
                }

                pHandles[moduleIndex] = handle;
                moduleIndex++;
            }
        }

        //void* ptr;

        //Memory.RhAllocateNewArray(MethodTable.Of<TypeManagerHandle[]>(), (uint)moduleCount, 0, out ptr);
        // Any potentially dehydrated MethodTables got rehydrated, we can safely use `new` now.
        var modules = new TypeManagerHandle[moduleCount];
        //var modules = Unsafe.AsRef<TypeManagerHandle[]>(ptr);
        for (int i = 0; i < modules.Length; i++)
            modules[i] = pHandles[i];
        return modules;
    }
    internal static unsafe TypeManagerHandle[] CreateTypeManagers(IntPtr osModule, ReadyToRunHeader** pModuleHeaders, int count, void** pClasslibFunctions, uint nClasslibFunctions)
    {
        // Count the number of modules so we can allocate an array to hold the TypeManager objects.
        // At this stage of startup, complex collection classes will not work.
        int moduleCount = 0;
        for (int i = 0; i < count; i++)
        {
            // The null pointers are sentinel values and padding inserted as side-effect of
            // the section merging. (The global static constructors section used by C++ has
            // them too.)
            if (pModuleHeaders[i] != (void*)IntPtr.Zero)
                moduleCount++;
        }

        // We cannot use the new keyword just yet, so stackalloc the array first
        var pHandles = stackalloc TypeManagerHandle[moduleCount];
        int moduleIndex = 0;
        for (int i = 0; i < count; i++)
        {
            if (pModuleHeaders[i] != (void*)IntPtr.Zero)
            {
                TypeManagerHandle handle = RhpCreateTypeManager(osModule, pModuleHeaders[i], pClasslibFunctions, nClasslibFunctions);

                pHandles[moduleIndex] = handle;
                moduleIndex++;
            }
        }

        // Any potentially dehydrated MethodTables got rehydrated, we can safely use `new` now.
        TypeManagerHandle[] modules = new TypeManagerHandle[moduleCount];
        for (int i = 0; i < moduleCount; i++)
            modules[i] = pHandles[i];

        return modules;
    }
}
