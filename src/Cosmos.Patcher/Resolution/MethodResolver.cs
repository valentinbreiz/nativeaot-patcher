using System.Linq;
using Cosmos.Patcher.Logging;
using Mono.Cecil;

namespace Cosmos.Patcher.Resolution;

/// <summary>
/// Resolves target methods and constructors from plug methods.
/// Handles parameter type matching for overload resolution.
/// </summary>
public class MethodResolver
{
    private readonly IBuildLogger _log;

    public MethodResolver(IBuildLogger log)
    {
        _log = log;
    }

    /// <summary>
    /// Determines if a plug method is an instance plug (has aThis parameter).
    /// </summary>
    public static bool IsInstancePlug(MethodDefinition plugMethod)
    {
        return plugMethod.Parameters.Any(p => p.Name == "aThis");
    }

    /// <summary>
    /// Finds a constructor in the target type that matches the plug method's signature.
    /// Matches by parameter count AND parameter types.
    /// </summary>
    public MethodDefinition? ResolveConstructor(TypeDefinition targetType, MethodDefinition plugMethod)
    {
        bool isInstance = IsInstancePlug(plugMethod);
        int plugStartIndex = isInstance ? 1 : 0;
        bool isStaticCtor = plugMethod.Name == "CCtor";

        _log.Debug($"Resolving constructor for {plugMethod.FullName} (Instance: {isInstance}, Static: {isStaticCtor})");

        foreach (var ctor in targetType.Methods.Where(m => m.IsConstructor))
        {
            // CCtor plugs must match static constructors
            if (isStaticCtor && !ctor.IsStatic)
                continue;

            // Ctor plugs must match instance constructors
            if (!isStaticCtor && ctor.IsStatic)
                continue;

            // Check parameter count
            int expectedParamCount = isInstance ? plugMethod.Parameters.Count - 1 : plugMethod.Parameters.Count;
            if (ctor.Parameters.Count != expectedParamCount)
                continue;

            // Check parameter types match
            if (ParameterTypesMatch(ctor, plugMethod, plugStartIndex))
            {
                _log.Debug($"Found matching constructor: {ctor.FullName}");
                return ctor;
            }
        }

        _log.Warn($"No matching constructor found for {plugMethod.FullName}");
        LogPlugParameters(plugMethod);
        return null;
    }

    /// <summary>
    /// Finds a method in the target type that matches the plug method's name and signature.
    /// Matches by name, parameter count AND parameter types.
    /// </summary>
    public MethodDefinition? ResolveMethod(TypeDefinition targetType, MethodDefinition plugMethod, string targetMethodName)
    {
        bool isInstance = IsInstancePlug(plugMethod);
        int plugStartIndex = isInstance ? 1 : 0;
        int expectedParamCount = isInstance ? plugMethod.Parameters.Count - 1 : plugMethod.Parameters.Count;

        _log.Debug($"Resolving method: {targetMethodName} (Instance: {isInstance})");

        foreach (var method in targetType.Methods.Where(m => m.Name == targetMethodName))
        {
            // Check parameter count
            if (method.Parameters.Count != expectedParamCount)
                continue;

            // Check parameter types match
            if (ParameterTypesMatch(method, plugMethod, plugStartIndex))
            {
                _log.Debug($"Found matching method: {method.FullName}");
                return method;
            }
        }

        _log.Warn($"Target method not found: {targetMethodName}");
        _log.Debug($"Expected parameters: {expectedParamCount}");
        LogExpectedParameterTypes(plugMethod, isInstance);
        return null;
    }

    /// <summary>
    /// Checks if the parameter types of the target method match the plug method.
    /// </summary>
    private static bool ParameterTypesMatch(MethodDefinition targetMethod, MethodDefinition plugMethod, int plugStartIndex)
    {
        for (int i = 0; i < targetMethod.Parameters.Count; i++)
        {
            var targetParam = targetMethod.Parameters[i];
            var plugParam = plugMethod.Parameters[i + plugStartIndex];

            if (targetParam.ParameterType.FullName != plugParam.ParameterType.FullName)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Logs the plug method's parameters for debugging.
    /// </summary>
    private void LogPlugParameters(MethodDefinition plugMethod)
    {
        _log.Debug($"Plug parameters: {string.Join(", ", plugMethod.Parameters.Select(p => p.ParameterType + " " + p.Name))}");
    }

    /// <summary>
    /// Logs the expected parameter types for debugging.
    /// </summary>
    private void LogExpectedParameterTypes(MethodDefinition plugMethod, bool isInstance)
    {
        _log.Debug($"Expected parameter types: {string.Join(", ", plugMethod.Parameters.Skip(isInstance ? 1 : 0).Select(p => p.ParameterType.FullName))}");
    }

    /// <summary>
    /// Dumps all overloads of a method for debugging purposes.
    /// </summary>
    public void DumpOverloads(TypeDefinition targetType, string methodName, MethodDefinition plugMethod)
    {
        bool isInstancePlug = IsInstancePlug(plugMethod);
        int expectedCount = plugMethod.Parameters.Count - (isInstancePlug ? 1 : 0);

        _log.Debug($"Available overloads in type: {targetType.FullName}, name: {methodName}");

        var overloads = targetType.Methods.Where(m => m.Name == methodName).ToArray();
        if (overloads.Length == 0)
        {
            _log.Debug("  (none)");
            return;
        }

        foreach (var m in overloads)
        {
            bool countOk = m.Parameters.Count == expectedCount;
            bool instOk = !isInstancePlug || !m.IsStatic;
            string signature = FormatMethodSignature(m);
            _log.Debug($"  - {signature}  [params:{m.Parameters.Count} {(countOk ? "OK" : "NO")}, instance:{(!m.IsStatic)} {(instOk ? "OK" : "NO")}]");
        }
    }

    /// <summary>
    /// Formats a method signature for display.
    /// </summary>
    public static string FormatMethodSignature(MethodDefinition m)
    {
        string owner = m.DeclaringType?.FullName ?? "?";
        string ret = m.ReturnType?.FullName ?? "void";
        string name = m.IsConstructor ? (m.IsStatic ? ".cctor" : ".ctor") : m.Name;
        string inst = m.IsStatic ? "static" : "instance";
        string parameters = "(" + string.Join(", ", m.Parameters.Select(p => p.ParameterType?.FullName ?? "?")) + ")";
        return $"{inst} {ret} {owner}::{name}{parameters}";
    }
}
