using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cosmos.Build.Analyzer.Patcher.Extensions;

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

    public static bool HasAttribute(this MemberDeclarationSyntax member, params string[] attributeNames) =>
      member.AttributeLists.SelectMany(a => a.Attributes).Any(a => attributeNames.Contains(a.Name.ToString()));
}
