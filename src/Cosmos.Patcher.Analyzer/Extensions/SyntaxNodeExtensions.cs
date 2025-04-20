using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Cosmos.Patcher.Analyzer.Utils;

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

    public static Location GetFullMethodLocation(this MethodDeclarationSyntax method)
    {
        int start = method.Identifier.SpanStart;

        int end;
        if (method.Body != null)
            end = method.Body.CloseBraceToken.Span.End;
        else if (method.ExpressionBody != null)
            end = method.ExpressionBody.Expression.Span.End;
        else
            end = method.ParameterList.Span.End;


        TextSpan span = TextSpan.FromBounds(start, end);

        SyntaxTree syntaxTree = method.SyntaxTree;
        string filePath = syntaxTree.FilePath;

        Location location = Location.Create(filePath, span, syntaxTree.GetLineSpan(span).Span);

        return location;
    }
}
