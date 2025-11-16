// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime;
using System.Runtime.CompilerServices;
using Internal.Runtime;

namespace Cosmos.Kernel.Core.Runtime;

internal static unsafe class Casting
{
    [RuntimeExport("RhTypeCast_AreTypesAssignable")]
    public static bool RhTypeCast_AreTypesAssignable(int typeHandleSrc, int typeHandleDest)
    {
        MethodTable* srcType = (MethodTable*)typeHandleSrc;
        MethodTable* destType = (MethodTable*)typeHandleDest;
        return destType->IsInterface
            ? RhTypeCast_IsInstanceOfInterface(srcType, destType)
            : RhTypeCast_IsInstanceOfClass(srcType, destType);
    }

    [RuntimeExport("RhTypeCast_IsInstanceOfAny")]
    public static object RhTypeCast_IsInstanceOfAny(object obj, int* pTypeHandles, int count)
    {
        MethodTable* type = obj.GetMethodTable();
        for (int i = 0; i < count; i++)
        {
            if (type == (MethodTable*)pTypeHandles[i])
                return obj;
        }

        return null;
    }

    [RuntimeExport("RhTypeCast_IsInstanceOfInterface")]
    public static bool RhTypeCast_IsInstanceOfInterface(object obj, int interfaceTypeHandle)
    {
        MethodTable* type = obj.GetMethodTable();
        MethodTable* interfaceType = (MethodTable*)interfaceTypeHandle;

        return RhTypeCast_IsInstanceOfInterface(type, interfaceType);
    }

    private static bool RhTypeCast_IsInstanceOfInterface(MethodTable* type, MethodTable* interfaceType)
    {
        while (type != null)
        {
            for (int i = 0; i < type->NumInterfaces; i++)
            {
                if (type->InterfaceMap[i] == interfaceType)
                    return true;
            }

            type = type->BaseType;
        }

        return false;
    }

    [RuntimeExport("RhTypeCast_CheckCastInterface")]
    public static object RhTypeCast_CheckCastInterface(object obj, int interfaceTypeHandle)
    {
        if (!RhTypeCast_IsInstanceOfInterface(obj, interfaceTypeHandle))
            throw new InvalidCastException();

        return obj;
    }

    [RuntimeExport("RhTypeCast_CheckCastClass")]
    public static object RhTypeCast_CheckCastClass(object obj, int typeHandle)
    {
        return RhTypeCast_IsInstanceOfClass(obj, typeHandle) ?? throw new InvalidCastException();
    }

    // Essential runtime functions needed by the linker
    [RuntimeExport("RhTypeCast_IsInstanceOfClass")]
    public static object RhTypeCast_IsInstanceOfClass(object obj, int classTypeHandle)
    {
        MethodTable* type = obj.GetMethodTable();
        MethodTable* classType = (MethodTable*)classTypeHandle;

        return RhTypeCast_IsInstanceOfClass(type, classType) ? obj : null;
    }

   private static bool RhTypeCast_IsInstanceOfClass(MethodTable* type, MethodTable* classType)
    {
        while (type != null)
        {
            if (type == classType)
                return true;

            type = type->BaseType;
        }

        return false;
    }
}
