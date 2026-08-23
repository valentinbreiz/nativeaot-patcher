// This code is licensed under the BSD 3-Clause license (see LICENSE for details)

using Microsoft.CodeAnalysis;

namespace Cosmos.Build.Analyzer.Patcher.Extensions;

public static class SymbolExtensions
{
    public static bool HasAttribute(this ISymbol symbol, params string[] attributeNames) => symbol.GetAttributes().Any(a => attributeNames.Contains(a?.AttributeClass?.Name));
}
