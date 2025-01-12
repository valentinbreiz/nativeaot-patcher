using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Liquip.Patcher.Analzyer;

public static class DiagnosticMessages
{
    public static DiagnosticDescriptor TypeNotFound = new(
        id: PlugAnalyzer.DiagnosticId,
        title: "Type Not Found",
        messageFormat: "The specified type '{0}' could not be located",
        category: "Naming",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Ensure that the type name is correct and that the type is accessible."
    );

    public static ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(TypeNotFound);
}
