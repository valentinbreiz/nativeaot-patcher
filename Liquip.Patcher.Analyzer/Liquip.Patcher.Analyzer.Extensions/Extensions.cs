using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Liquip.Patcher.Analyzer.Extensions;

public static class Extensions
{
    public static bool TryGetMemberByName<T>(this ClassDeclarationSyntax declaration, string name, out T member)
        where T : MemberDeclarationSyntax
    {
        member = declaration.Members
            .OfType<T>()
            .FirstOrDefault(m => (m is MethodDeclarationSyntax method && method.Identifier.ValueText == name) ||
                                 (m is PropertyDeclarationSyntax property && property.Identifier.ValueText == name) ||
                                 (m is FieldDeclarationSyntax field && field.Declaration.Variables.Any(v => v.Identifier.ValueText == name))
            );

        return member != null;
    }

    public static bool TryGetMemberByName<T>(this INamespaceOrTypeSymbol symbol, string name, out T memberSymbol) where T : ISymbol
    {
        memberSymbol = symbol.GetMembers().OfType<T>().FirstOrDefault(s => s.Name == name);
        return memberSymbol != null;
    }

    public static bool Any<T>(this IEnumerable<T> enumerable, Func<T, bool> predicate, out T? value)
    {
        foreach (T item in enumerable)
        {
            if (predicate(item))
            {
                value = item;
                return true;
            }
        }

        value = default;
        return false;
    }

    public static bool IsPlugClass(this ClassDeclarationSyntax classDeclaration) => classDeclaration.AttributeLists
    .Any(list => list.Attributes.Any(attr => attr.Name.ToString() == "Plug" || attr.Name.ToString() == "PlugAttribute"));

}

