using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Cosmos.Patcher.Analyzer.Extensions;

public static class SyntaxNodeExtensions
{
    public static bool TryGetMemberByName<T>(this ClassDeclarationSyntax declaration, string name, out T? member)
        where T : MemberDeclarationSyntax
    {
        foreach (MemberDeclarationSyntax memberDeclarationSyntax in declaration.Members)
        {
            if (memberDeclarationSyntax is not T memberDeclaration)
                continue;

            if (memberDeclaration.GetName() != name)
                continue;

            member = memberDeclaration;
            return true;
        }
        member = null;
        return false;
    }


    public static string? GetName(this MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax method => method.Identifier.ValueText,
        PropertyDeclarationSyntax property => property.Identifier.ValueText,
        FieldDeclarationSyntax field => field.Declaration.Variables
            .FirstOrDefault()?.Identifier.ValueText,
        _ => null
    };

}
