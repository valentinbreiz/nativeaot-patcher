using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Liquip.XSharp.SourceGenerator;

public static class GeneratorSyntaxContextEx
{
    /// <summary>
    /// Checks whether the Node is annotated with the [PlugAttributeFound] attribute and maps syntax context to the specific node type (ClassDeclarationSyntax).
    /// </summary>
    /// <param name="context">Syntax context, based on CreateSyntaxProvider predicate</param>
    /// <returns>The specific cast and whether the attribute was found.</returns>
    public static (ClassDeclarationSyntax, bool PlugAttributeFound) GetClassDeclaration<T>(
        this GeneratorSyntaxContext context)
    {
        ClassDeclarationSyntax? classDeclarationSyntax = (ClassDeclarationSyntax)context.Node;

        // Go through all attributes of the class.
        foreach (AttributeSyntax? attributeSyntax in
                 classDeclarationSyntax.AttributeLists.SelectMany(i => i.Attributes))
        {
            if (context.SemanticModel.GetSymbolInfo(attributeSyntax).Symbol is not IMethodSymbol attributeSymbol)
            {
                continue; // if we can't get the symbol, ignore it
            }

            string? attributeName = attributeSymbol.ContainingType.ToDisplayString();

            // Check the full name of the [Report] attribute.
            if (attributeName == typeof(T).FullName)
            {
                return (classDeclarationSyntax, true);
            }
        }

        return (classDeclarationSyntax, false);
    }

    public static (MethodDeclarationSyntax, bool PlugAttributeFound) GetMethodDeclaration<T>(
        this GeneratorSyntaxContext context)
    {
        MethodDeclarationSyntax? methodDeclarationSyntax = (MethodDeclarationSyntax)context.Node;

        // Go through all attributes of the class.
        foreach (AttributeSyntax? attributeSyntax in methodDeclarationSyntax
                     .AttributeLists
                     .SelectMany(i => i.Attributes))
        {
            if (context.SemanticModel.GetSymbolInfo(attributeSyntax).Symbol is not IMethodSymbol attributeSymbol)
            {
                continue; // if we can't get the symbol, ignore it
            }

            string? attributeName = attributeSymbol.ContainingType.ToDisplayString();

            // Check the full name of the [Report] attribute.
            if (attributeName == typeof(T).FullName)
            {
                return (methodDeclarationSyntax, true);
            }
        }

        return (methodDeclarationSyntax, false);
    }
}
