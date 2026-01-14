using System.Collections.Generic;
using System.Linq;
using Cosmos.Patcher.Logging;
using Mono.Cecil;

namespace Cosmos.Patcher.Debug;

/// <summary>
/// Debug utilities for logging and dumping IL code.
/// </summary>
public static class DebugHelpers
{
    /// <summary>
    /// Formats method parameters for display.
    /// </summary>
    public static string FormatParameters(IEnumerable<ParameterDefinition> parameters)
    {
        return "(" + string.Join(", ", parameters.Select(p => p.ParameterType?.FullName ?? "?")) + ")";
    }

    /// <summary>
    /// Formats a method signature for display.
    /// </summary>
    public static string FormatMethod(MethodDefinition method)
    {
        string owner = method.DeclaringType?.FullName ?? "?";
        string ret = method.ReturnType?.FullName ?? "void";
        string name = method.IsConstructor ? (method.IsStatic ? ".cctor" : ".ctor") : method.Name;
        string inst = method.IsStatic ? "static" : "instance";
        return $"{inst} {ret} {owner}::{name}{FormatParameters(method.Parameters)}";
    }

    /// <summary>
    /// Dumps the IL instructions of a method.
    /// </summary>
    public static void DumpIL(IBuildLogger log, MethodDefinition method)
    {
        log.Debug($"IL for method: {method.FullName}");
        foreach (var instruction in method.Body.Instructions)
        {
            log.Debug($"  {instruction}");
        }
    }

    /// <summary>
    /// Dumps all members of a type.
    /// </summary>
    public static void DumpTypeMembers(IBuildLogger log, TypeDefinition type)
    {
        log.Debug($"--- Members of {type.FullName} ---");
        foreach (var member in GetMembers(type))
        {
            log.Debug($"  {member.GetType().Name}: {member.FullName}");
            if (member is MethodDefinition { HasBody: true } method)
            {
                DumpIL(log, method);
            }
        }
        log.Debug($"--- End members ---");
    }

    /// <summary>
    /// Gets all members of a type (methods, properties, fields).
    /// </summary>
    private static IEnumerable<IMemberDefinition> GetMembers(TypeDefinition type)
    {
        foreach (var method in type.Methods)
            yield return method;
        foreach (var property in type.Properties)
            yield return property;
        foreach (var field in type.Fields)
            yield return field;
    }
}
