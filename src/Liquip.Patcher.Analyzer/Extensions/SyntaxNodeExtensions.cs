using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Liquip.Patcher.Analyzer.Utils;

public static class Extensions
{
    public static bool TryGetMemberByName<T>(this ClassDeclarationSyntax declaration, string name, out T? member)
        where T : MemberDeclarationSyntax
    {
        foreach (MemberDeclarationSyntax memberDeclarationSyntax in declaration.Members)
        {
            if (memberDeclarationSyntax is not T memberDeclaration)
                continue;

            if (GetMemberName(memberDeclaration) == name)
            {
                member = memberDeclaration;
                return true;
            }
        }
        member = null;
        return false;
    }


    private static string? GetMemberName(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax method => method.Identifier.ValueText,
        PropertyDeclarationSyntax property => property.Identifier.ValueText,
        FieldDeclarationSyntax field => field.Declaration.Variables
            .FirstOrDefault()?.Identifier.ValueText,
        _ => null
    };



    public static bool TryFindNode<T>(this SyntaxNode node, TextSpan span, out T? value) where T : SyntaxNode?
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
