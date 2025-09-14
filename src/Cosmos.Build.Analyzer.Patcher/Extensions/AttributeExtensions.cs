using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Cosmos.Build.Analyzer.Patcher.Extensions;

public static class AttributeExtensions
{
    public static T? GetArgument<T>(this AttributeSyntax attribute, SyntaxNodeAnalysisContext context, string named,
        int positional = 0)
    {
        ExpressionSyntax? expression = positional >= 0 && positional <= attribute.ArgumentList?.Arguments.Count - 1
                ? attribute.ArgumentList.Arguments[positional].Expression
            : attribute.ArgumentList?.Arguments
                .FirstOrDefault(a => string.Equals((a.NameEquals?.Name ?? a.NameColon?.Name)?.ToString(), named, StringComparison.InvariantCultureIgnoreCase))
                .Expression;

        return expression switch
        {
            MemberAccessExpressionSyntax { Name: { } name } when typeof(T).IsEnum
                => (T?)Enum.Parse(typeof(T), name.ToString()),
            LiteralExpressionSyntax literal
                => (T?)literal.Token.Value,
            TypeOfExpressionSyntax typeOf
                => (T?)(object?)(
                    context.SemanticModel.GetSymbolInfo(typeOf.Type).Symbol is ITypeSymbol symbol
                        ? symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted))
                        : "Unknown"
                ),
            _ => default
        };
    }

    public static bool GetArgument<T>(this AttributeSyntax attribute, SyntaxNodeAnalysisContext context, string named, int positional,
         out T? value)
    {
        value = GetArgument<T>(attribute, context, named, positional);
        return value != null && (value is not string str || !string.IsNullOrEmpty(str));
    }
    public static T? GetArgument<T>(this AttributeData attributeData, string named = "", int positional = 0)
    {
        TypedConstant target = positional >= 0 && attributeData.ConstructorArguments.Length > positional
            ? attributeData.ConstructorArguments[positional]
            : attributeData.NamedArguments
            .FirstOrDefault(kv => string.Equals(kv.Key, named, StringComparison.InvariantCultureIgnoreCase)).Value;

        if (target.Value == null)
            return default;

        return target.Value switch
        {
            T v => v,
            _ => typeof(T).IsEnum ? (T)Enum.Parse(typeof(T), target.Value.ToString()!) : default
        };
    }
}
