// This code is licensed under MIT license (see LICENSE for details)

using Microsoft.CodeAnalysis;

namespace Cosmos.Patcher.Analyzer.Extensions;

public static class SymbolExtensions
{
    public static bool HasAttribute(this ISymbol symbol, params string[] attributeNames) => symbol.GetAttributes().Any(a => attributeNames.Contains(a?.AttributeClass?.Name));
}
