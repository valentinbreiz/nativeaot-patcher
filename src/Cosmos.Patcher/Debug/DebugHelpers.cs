using Cosmos.Patcher.Extensions;
using Cosmos.Patcher.Logging;
using Mono.Cecil;
using Mono.Cecil.Cil;

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
    public static string FormatMethodSignature(MethodDefinition method)
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
        foreach (Instruction? instruction in method.Body.Instructions)
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
        foreach (IMemberDefinition member in type.GetMembers())
        {
            log.Debug($"  {member.GetType().Name}: {member.FullName}");
            if (member is MethodDefinition { HasBody: true } method)
            {
                DumpIL(log, method);
            }
        }
        log.Debug($"--- End members ---");
    }
}
