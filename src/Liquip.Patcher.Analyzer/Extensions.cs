using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Liquip.Patcher.Analyzer.Extensions;

public static class Extensions
{
    public static bool TryGetMemberByName<T>(this ClassDeclarationSyntax declaration, string name, out T member)
   where T : MemberDeclarationSyntax => (member = declaration.Members
            .OfType<T>()
            .FirstOrDefault(m => (m is MethodDeclarationSyntax method && method.Identifier.ValueText == name) ||
                                 (m is PropertyDeclarationSyntax property && property.Identifier.ValueText == name) ||
                                 (m is FieldDeclarationSyntax field && field.Declaration.Variables.Any(v => v.Identifier.ValueText == name))
            )) != null;

    public static bool Any<T>(this IEnumerable<T> enumerable, Func<T, bool> predicate, out T value)
    {
        IEnumerator<T> enumerator = enumerable.GetEnumerator();
        while (enumerator.MoveNext())
        {
            if (!predicate(enumerator.Current)) continue;
            value = enumerator.Current;
            return true;
        }
        value = default;
        return false;
    }

    public static bool TryFindNode<T>(this SyntaxNode node, TextSpan span, out T value) where T : SyntaxNode?
    {
        try
        {
            value = (T)node.FindNode(span);
            return true;
        }
        catch
        {
            value = default;
            return false;
        }
    }
}
