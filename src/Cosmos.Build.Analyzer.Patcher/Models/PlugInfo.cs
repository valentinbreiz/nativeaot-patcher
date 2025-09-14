using Microsoft.CodeAnalysis;

namespace Cosmos.Build.Analyzer.Patcher.Models;

public record PlugInfo(bool TargetExternal, INamedTypeSymbol PlugSymbol);
