using System.Runtime;
using System.Runtime.CompilerServices;
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
        TypeManager typeManager = new(osModule, moduleHeader, pClasslibFunctions, nClasslibFunctions);

        return new TypeManagerHandle(&typeManager);
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

#if NET10_0_OR_GREATER
    // We could use this function to rehydratate data from modules, if dehydrated data section is present.
    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "RehydrateData")]
    private static extern void RehydrateData([UnsafeAccessorType("Internal.Runtime.CompilerHelpers.StartupCodeHelpers")] object obj, IntPtr dehydratedData, int length);
#endif
}
