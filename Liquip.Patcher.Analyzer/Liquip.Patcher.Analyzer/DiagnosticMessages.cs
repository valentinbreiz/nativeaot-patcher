using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Liquip.Patcher.Analyzer;

public sealed class DiagnosticMessages
{
    public static readonly DiagnosticDescriptor TypeNotFound = new(
        id: "NAOT0001",
        title: "Type Not Found",
        messageFormat: "The specified type '{0}' could not be located",
        category: "Naming",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Ensure that the type name is correct and that the type is accessible."
    );

    public static readonly DiagnosticDescriptor MethodNeedsPlug = new(
        id: "NAOT0002",
        title: "Method Needs Plug",
        messageFormat: "Method '{0}' in class '{1}' requires a plug",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Ensure that the method has a corresponding plug. See http://www.gocosmos.org/docs/plugs/missing/ for more information."
    );

    public static readonly DiagnosticDescriptor PlugNotStatic = new(
        id: "NAOT0003",
        title: "Plug Not Static",
        messageFormat: "Plug '{0}' should be static",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Ensure that the plug is static if it only contains static members."
    );



    public static readonly DiagnosticDescriptor PlugNameDoesNotMatch = new(
    id: "NAOT0004",
    title: "Plug Name Does Not Match",
    messageFormat: "Plug '{0}' should be renamed to '{1}'",
    category: "Naming",
    defaultSeverity: DiagnosticSeverity.Info,
    isEnabledByDefault: true,
    description: "Ensure that the plug name matches the plugged class name."
);

    public static readonly DiagnosticDescriptor MethodNotImplemented = new(
        id: "NAOT0005",
        title: "Method Not Implemented",
        messageFormat: "Method '{0}' does not exist in '{1}'",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Ensure that the method name is correct and that the method exists."
    );

    public static ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(TypeNotFound, MethodNeedsPlug, PlugNotStatic, PlugNameDoesNotMatch, MethodNotImplemented);
}
