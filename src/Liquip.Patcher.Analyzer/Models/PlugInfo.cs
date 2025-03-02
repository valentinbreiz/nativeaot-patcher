using Microsoft.CodeAnalysis;

namespace Liquip.Patcher.Analyzer.Models;

public record PlugInfo(bool IsExternal, INamedTypeSymbol PluggedSymbol)
{
    public readonly bool IsExternal = IsExternal;
    public readonly INamedTypeSymbol PluggedSymbol = PluggedSymbol;
}
