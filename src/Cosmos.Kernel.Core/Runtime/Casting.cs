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
            ? IsInstanceOfInterface(typeHandleSrc, typeHandleDest)
            : IsInstanceOfClass(typeHandleSrc, typeHandleDest);
    }

    [RuntimeExport("RhTypeCast_IsInstanceOfAny")]
    public static object RhTypeCast_IsInstanceOfAny(object obj, MethodTable** pTypeHandles, int count)
    {
        MethodTable* type = obj.GetMethodTable();
        for (int i = 0; i < count; i++)
        {
            if (RhTypeCast_AreTypesAssignable(type, pTypeHandles[i]))
                return obj;
        }

        return null;
    }

    [RuntimeExport("RhTypeCast_IsInstanceOfInterface")]
    public static bool RhTypeCast_IsInstanceOfInterface(object obj, MethodTable* interfaceTypeHandle)
    {
        MethodTable* type = obj.GetMethodTable();
        return IsInstanceOfInterface(type, interfaceTypeHandle);
    }


    // Essential runtime functions needed by the linker
    [RuntimeExport("RhTypeCast_IsInstanceOfClass")]
    public static object RhTypeCast_IsInstanceOfClass(object obj, MethodTable* classTypeHandle)
    {
        MethodTable* type = obj.GetMethodTable();
        return IsInstanceOfClass(type, classTypeHandle) ? obj : null;
    }

    [RuntimeExport("RhTypeCast_CheckCastInterface")]
    public static object RhTypeCast_CheckCastInterface(object obj, MethodTable* interfaceTypeHandle)
    {
        return !RhTypeCast_IsInstanceOfInterface(obj, interfaceTypeHandle)
            ? throw new InvalidCastException()
            : obj;
    }

    [RuntimeExport("RhTypeCast_CheckCastClass")]
    public static object RhTypeCast_CheckCastClass(object obj, MethodTable* typeHandle)
    {
        return RhTypeCast_IsInstanceOfClass(obj, typeHandle) ?? throw new InvalidCastException();
    }

    [RuntimeExport("RhTypeCast_CheckCastClassSpecial")]
    static unsafe object RhTypeCast_CheckCastClassSpecial(object obj, MethodTable* typeHandle, bool fThrow)
    {
        if (IsInstanceOfClass(obj.GetMethodTable(), typeHandle))
            return obj;

        return fThrow ? throw new InvalidCastException() : null;
    }

    [RuntimeExport("RhTypeCast_CheckCastAny")]
    static unsafe object RhTypeCast_CheckCastAny(object obj, MethodTable* typeHandle)
    {
        return obj;
    }

    private static bool IsInstanceOfInterface(MethodTable* type, MethodTable* interfaceType)
    {
        while (type != null)
        {
            for (int i = 0; i < type->NumInterfaces; i++)
            {
                MethodTable* interfaceImpl = type->InterfaceMap[i];
                if (interfaceImpl != interfaceType)
                    continue;

                if (interfaceType->GenericDefinition->GenericParameterCount == 0)
                    return true;

            }

            type = type->BaseType;
        }

        return false;
    }

    private static bool IsInstanceOfClass(MethodTable* type, MethodTable* classType)
    {
        while (type != null)
        {
            if (type != classType)
            {
                type = type->BaseType;
                continue;
            }

            if (classType->GenericParameterCount == 0)
                return true; // No generics, so it's an exact match

            return AreGenericsAssignable(type, classType); // Check generics
        }

        return false;
    }

    private static bool AreGenericsAssignable(MethodTable* sourceType, MethodTable* targetType)
    {
        for (int i = 0; i < targetType->GenericParameterCount; i++)
        {
            MethodTable* sourceGeneric = sourceType->GenericArguments[i]; // Generic of the cast target
            MethodTable* targetGeneric = targetType->GenericArguments[i]; // Generic of the cast type;

            if (!targetGeneric->HasGenericVariance)
                return sourceGeneric == targetGeneric; // Nonvariant generic, check if they are the same


            GenericVariance targetGenericVariance = targetGeneric->GenericVariance[i];
            bool assignable = targetGenericVariance == GenericVariance.Covariant &&
                              RhTypeCast_AreTypesAssignable(sourceGeneric, targetGeneric) ||
                              targetGenericVariance == GenericVariance.Contravariant &&
                              RhTypeCast_AreTypesAssignable(targetGeneric, sourceGeneric) ||
                              (targetGeneric->IsArray && sourceGeneric->IsArray) &&
                              RhTypeCast_AreTypesAssignable(sourceGeneric->RelatedParameterType,
                                  targetGeneric->RelatedParameterType); // Array covariance

            if (!assignable)
                return false;
        }

        return true;
    }
}
