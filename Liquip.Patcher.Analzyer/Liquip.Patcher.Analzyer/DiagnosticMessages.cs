using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Liquip.Patcher.Analzyer;

public static class DiagnosticMessages
{
    public static DiagnosticDescriptor TypeNotFound = new(
        id: "NAOT0001",
        title: "Type Not Found",
        messageFormat: "The specified type '{0}' could not be located",
        category: "Naming",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Ensure that the type name is correct and that the type is accessible."
    );

    public static DiagnosticDescriptor MethodNeedsPlug = new(
        id: "NAOT0002",
        title: "Method Needs Plug",
        messageFormat: "Method '{0}' in class '{1}' requires a plug",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Ensure that the method has a corresponding plug. See http://www.gocosmos.org/docs/plugs/missing/ for more information."
    );

    public static DiagnosticDescriptor ClassNeedsPlug = new(
        id: "NAOT0003",
        title: "Class Needs Plug",
        messageFormat: "Class '{0}' requires a plug",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Ensure that the class has a corresponding plug. See http://www.gocosmos.org/docs/plugs/missing/ for more information."
    );

    public static DiagnosticDescriptor PlugNotStatic = new(
        id: "NAOT0004",
        title: "Plug Not Static",
        messageFormat: "Plug '{0}' should be static",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Ensure that the plug is static if it only contains static members."
    );

    public static DiagnosticDescriptor PlugNameNeedsImplSuffix = new(
        id: "NAOT0005",
        title: "Plug Name Needs Impl Suffix",
        messageFormat: "Plug '{0}' should have an Impl suffix",
        category: "Naming",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Ensure that the plug name ends with Impl."
    );

    public static DiagnosticDescriptor MethodNotImplemented = new(
        id: "NAOT0006",
        title: "Method Not Implemented",
        messageFormat: "Plug class '{0}' has methods that are not implemented in '{1}'",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Ensure that all methods in the plug class are implemented in the target class."
    );

    public static ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(TypeNotFound, MethodNeedsPlug, ClassNeedsPlug, PlugNotStatic, PlugNameNeedsImplSuffix, MethodNotImplemented);
}
