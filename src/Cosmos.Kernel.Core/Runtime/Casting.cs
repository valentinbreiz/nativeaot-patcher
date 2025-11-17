// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime;
using Internal.Runtime;

namespace Cosmos.Kernel.Core.Runtime;

internal static unsafe class Casting
{
    [RuntimeExport("RhTypeCast_AreTypesAssignable")]
    public static bool RhTypeCast_AreTypesAssignable(MethodTable* typeHandleSrc, MethodTable* typeHandleDest)
    {
        return typeHandleDest->IsInterface
            ? RhTypeCast_IsInstanceOfInterface(typeHandleSrc, typeHandleDest)
            : RhTypeCast_IsInstanceOfClass(typeHandleSrc, typeHandleDest);
    }

    [RuntimeExport("RhTypeCast_IsInstanceOfAny")]
    public static object RhTypeCast_IsInstanceOfAny(object obj, MethodTable** pTypeHandles, int count)
    {
        MethodTable* type = obj.GetMethodTable();
        for (int i = 0; i < count; i++)
        {
            MethodTable* typeHandle = pTypeHandles[i];
            if ((typeHandle->IsInterface && RhTypeCast_IsInstanceOfInterface(type, typeHandle)) ||
                RhTypeCast_IsInstanceOfClass(type, pTypeHandles[i]))
                return obj;
        }

        return null;
    }

    [RuntimeExport("RhTypeCast_IsInstanceOfInterface")]
    public static bool RhTypeCast_IsInstanceOfInterface(object obj, MethodTable* interfaceTypeHandle)
    {
        MethodTable* type = obj.GetMethodTable();
        return RhTypeCast_IsInstanceOfInterface(type, interfaceTypeHandle);
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
    public static object RhTypeCast_CheckCastInterface(object obj, MethodTable* interfaceTypeHandle)
    {
        if (!RhTypeCast_IsInstanceOfInterface(obj, interfaceTypeHandle))
            throw new InvalidCastException();

        return obj;
    }

    [RuntimeExport("RhTypeCast_CheckCastClass")]
    public static object RhTypeCast_CheckCastClass(object obj, MethodTable* typeHandle)
    {
        return RhTypeCast_IsInstanceOfClass(obj, typeHandle) ?? throw new InvalidCastException();
    }

    // Essential runtime functions needed by the linker
    [RuntimeExport("RhTypeCast_IsInstanceOfClass")]
    public static object RhTypeCast_IsInstanceOfClass(object obj, MethodTable* classTypeHandle)
    {
        MethodTable* type = obj.GetMethodTable();
        return RhTypeCast_IsInstanceOfClass(type, classTypeHandle) ? obj : null;
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
