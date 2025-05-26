using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Cosmos.Patcher.Analyzer.Extensions;

public static class AttributeExtensions
{
    public static T? GetAttributeValue<T>(this AttributeSyntax attribute, object indexOrString,
        SyntaxNodeAnalysisContext context)
    {
        ExpressionSyntax? expression = GetArgumentExpression(attribute, indexOrString);
        return expression != null ? GetValueFromExpression<T>(expression, context) : default;
    }

    public static bool GetAttributeValue<T>(this AttributeSyntax attribute, object indexOrString,
        SyntaxNodeAnalysisContext context, out T? value)
    {
        value = GetAttributeValue<T>(attribute, indexOrString, context);
        return value != null && (value is not string str || !string.IsNullOrEmpty(str));
    }

    public static T? GetAttributeValue<T>(this AttributeData attribute, object indexOrString)
    {
        TypedConstant argument = GetConstructorArgument(attribute, indexOrString);
        return argument.Kind == TypedConstantKind.Error ? default : ConvertArgumentValue<T>(argument);
    }

    private static T? GetValueFromExpression<T>(ExpressionSyntax expression, SyntaxNodeAnalysisContext context) =>
        expression switch
        {
            MemberAccessExpressionSyntax { Name: { } name } when typeof(T).IsEnum
                => ParseEnum<T>(name.ToString()),
            LiteralExpressionSyntax literal
                => (T?)literal.Token.Value,
            TypeOfExpressionSyntax typeOf
                => (T?)(object?)GetTypeFromTypeOf(typeOf, context),
            _ => default
        };

    private static string? GetTypeFromTypeOf(TypeOfExpressionSyntax typeOf, SyntaxNodeAnalysisContext context)
    {
        ITypeSymbol? symbol = context.SemanticModel.GetSymbolInfo(typeOf.Type).Symbol as ITypeSymbol;
        return symbol is not null
            ? symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted))
            : "Unknown";
    }

    private static T? ParseEnum<T>(string name)
    {
        T value;
        return (value = (T)Enum.Parse(typeof(T), name)) != null ? value : default;
    }

    private static TypedConstant GetConstructorArgument(AttributeData attribute, object indexOrString) =>
        indexOrString switch
        {
            int index when index >= 0 && index < attribute.ConstructorArguments.Length
                => attribute.ConstructorArguments[index],
            string name
                => attribute.NamedArguments.FirstOrDefault(kvp =>
                    kvp.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value,
            _ => default
        };

    private static ExpressionSyntax? GetArgumentExpression(AttributeSyntax attribute, object indexOrString) =>
        indexOrString switch
        {
            int index when attribute.ArgumentList?.Arguments.Count > index
                => attribute.ArgumentList.Arguments[index].Expression,
            string name => attribute.ArgumentList?.Arguments
                .FirstOrDefault(a => (a.NameEquals?.Name ?? a.NameColon?.Name)?.ToString() == name)?
                .Expression,
            _ => null
        };

    private static T? ConvertArgumentValue<T>(TypedConstant argument)
    {
        if (argument.Value is T value)
        {
            return value;
        }

        return typeof(T).IsEnum && argument.Value != null ? ConvertEnum<T>(argument.Value)
            : typeof(T).Name == "Type" && argument.Value is ITypeSymbol type ? (T?)type
            : default;
    }

    private static T? ConvertEnum<T>(object value)
        => (T?)Enum.ToObject(typeof(T), value);


}
