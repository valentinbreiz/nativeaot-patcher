using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Liquip.Patcher.Analzyer;

public static class DiagnosticMessages
{
    public static DiagnosticDescriptor TypeNotFound = new(
        id: PatcherAnalyzer.DiagnosticId,
        title: "Type Not Found",
        messageFormat: "The specified type '{0}' could not be located",
        category: "Naming",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Ensure that the type name is correct and that the type is accessible."
    );

    public static DiagnosticDescriptor MethodNeedsPlug = new(
        id: PatcherAnalyzer.DiagnosticId,
        title: "Method Needs Plug",
        messageFormat: "The method '{0}' requires a plug",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Ensure that the method has a corresponding plug. See http://www.gocosmos.org/docs/plugs/missing/ for more information."
    );

    public static DiagnosticDescriptor ClassNotStatic = new(
        id: PatcherAnalyzer.DiagnosticId,
        title: "Class Not Static",
        messageFormat: "The plug impl class '{0}' should be static",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Ensure that the class is static if it only contains static members."
    );

    public static ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(TypeNotFound, MethodNeedsPlug, ClassNotStatic);
}
