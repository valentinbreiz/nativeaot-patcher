using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Cosmos.Patcher.Analyzer;

public sealed class DiagnosticMessages
{
    public static readonly DiagnosticDescriptor TypeNotFound = new(
        "NAOT0001",
        "Type Not Found",
        "The specified type '{0}' could not be located",
        "Naming",
        DiagnosticSeverity.Error,
        true,
        "Ensure that the type name is correct and that the type is accessible."
    );

    public static readonly DiagnosticDescriptor MethodNeedsPlug = new(
        "NAOT0002",
        "Method Needs Plug",
        "Method '{0}' in class '{1}' requires a plug",
        "Usage",
        DiagnosticSeverity.Error,
        true,
        "Ensure that the method has a corresponding plug. See http://www.gocosmos.org/docs/plugs/missing/ for more information."
    );

    public static readonly DiagnosticDescriptor PlugNameDoesNotMatch = new(
        "NAOT0004",
        "Plug Name Does Not Match",
        "Plug '{0}' should be renamed to '{1}'",
        "Naming",
        DiagnosticSeverity.Info,
        true,
        "Ensure that the plug name matches the plugged class name."
    );

    public static readonly DiagnosticDescriptor MethodNotImplemented = new(
        "NAOT0005",
        "Method Not Implemented",
        "Method '{0}' does not exist in '{1}'",
        "Usage",
        DiagnosticSeverity.Info,
        true,
        "Ensure that the method name is correct and that the method exists."
    );


    public static readonly DiagnosticDescriptor StaticConstructorTooManyParams = new(
        "NAOT0006",
        "Static Constructor Has Too Many Parameters",
        "The static constructor '{0}' contains too many parameters. A static constructor must not have more than one parameter.",
        "Usage",
        DiagnosticSeverity.Error,
        true,
        "A static constructor should have at most one parameter."
    );


    public static ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(TypeNotFound,
        MethodNeedsPlug, PlugNameDoesNotMatch, MethodNotImplemented, StaticConstructorTooManyParams);
}
