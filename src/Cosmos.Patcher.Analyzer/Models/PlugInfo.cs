using Microsoft.CodeAnalysis;

namespace Cosmos.Patcher.Analyzer.Models;

public record PlugInfo(bool IsExternal, INamedTypeSymbol PlugSymbol);
