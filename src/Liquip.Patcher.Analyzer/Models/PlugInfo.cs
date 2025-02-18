using Microsoft.CodeAnalysis;

namespace Liquip.Patcher.Analyzer.Models;

public record PlugInfo(bool IsExternal, INamedTypeSymbol PlugSymbol);
