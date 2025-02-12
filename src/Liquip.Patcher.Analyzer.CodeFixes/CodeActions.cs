using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using System.Xml.Linq;

internal static class CodeActions
{
    public static async Task<Solution> RemoveExtraParameters(Document document, SyntaxNode declaration)
    {
        if (declaration is not MethodDeclarationSyntax methodDeclaration) return document.Project.Solution;

        DocumentEditor editor = await DocumentEditor.CreateAsync(document).ConfigureAwait(false);
        editor.ReplaceNode(methodDeclaration, methodDeclaration.WithParameterList(
            methodDeclaration.ParameterList.WithParameters(
                SyntaxFactory.SeparatedList<ParameterSyntax>(methodDeclaration.ParameterList.Parameters.Where(p => p.Identifier.Text != "aThis")))));
        return editor.GetChangedDocument().Project.Solution;
    }

    public static async Task<Solution> RenamePlug(Document document, SyntaxNode declaration, CancellationToken c, Diagnostic diagnostic)
    {
        if (declaration is not ClassDeclarationSyntax classDeclaration) return document.Project.Solution;

        if (!diagnostic.Properties.TryGetValue("ExpectedName", out string? expectedName)) return document.Project.Solution;

        DocumentEditor editor = await DocumentEditor.CreateAsync(document, c).ConfigureAwait(false);

        editor.ReplaceNode(classDeclaration, classDeclaration.WithIdentifier(SyntaxFactory.Identifier(expectedName!)));
        return editor.GetChangedDocument().Project.Solution;
    }

    public static async Task<Solution> PlugMethod(Document document, SyntaxNode declaration, CancellationToken c, Diagnostic diagnostic, XDocument currentProject)
    {
        SyntaxNode? root = await document.GetSyntaxRootAsync(c).ConfigureAwait(false);
        if (root == null || !diagnostic.Properties.TryGetValue("MethodName", out string? methodName) || !diagnostic.Properties.TryGetValue("ClassName", out string? className))
        {
            return document.Project.Solution;
        }

        MethodDeclarationSyntax plugMethod = CreatePlugMethod(methodName!);

        if (declaration is ClassDeclarationSyntax classDeclaration)
        {
            return await PlugMethodOnPlug((classDeclaration, document), plugMethod);
        }

        DocumentEditor editor = await DocumentEditor.CreateAsync(document, c).ConfigureAwait(false);
        BaseNamespaceDeclarationSyntax? namespaceDeclaration = root
                .ChildNodes()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .FirstOrDefault();

        if (namespaceDeclaration == null) return document.Project.Solution;

        if (diagnostic.Properties.TryGetValue("PlugClass", out string? plugClassName))
        {
            (ClassDeclarationSyntax PlugClass, Document Document)? plugInfo = await GetPlugClass(plugClassName, document, currentProject);
            if (plugInfo == null) return document.Project.Solution;
            return await PlugMethodOnPlug(plugInfo.Value, plugMethod);
        }

        ClassDeclarationSyntax newPlug = SyntaxFactory.ClassDeclaration($"{className}Impl")
            .AddAttributeLists(SyntaxFactory.AttributeList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Attribute(SyntaxFactory.ParseName("Plug"))
                        .AddArgumentListArguments(
                            SyntaxFactory.AttributeArgument(
                                SyntaxFactory.LiteralExpression(
                                    SyntaxKind.StringLiteralExpression,
                                    SyntaxFactory.Literal($"{namespaceDeclaration.Name}.{className}")))))))
            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.StaticKeyword))
            .AddMembers(plugMethod);

        editor.ReplaceNode(namespaceDeclaration, namespaceDeclaration.AddMembers(newPlug));
        return editor.GetChangedDocument().Project.Solution;
    }

    private static MethodDeclarationSyntax CreatePlugMethod(string name)
    {
        return SyntaxFactory.MethodDeclaration(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)), name)
            .WithBody(SyntaxFactory.Block())
            .WithParameterList(
                SyntaxFactory.ParameterList(
                    SyntaxFactory.SingletonSeparatedList<ParameterSyntax>(
                        SyntaxFactory.Parameter(SyntaxFactory.ParseToken("instance"))
                            .WithType(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ObjectKeyword))))))
            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.StaticKeyword));
    }

    private static async Task<(ClassDeclarationSyntax PlugClass, Document Document)?> GetPlugClass(string? plugClassName, Document document, XDocument currentProject)
    {
        if (plugClassName == null) return null;

        SyntaxNode? root = await document.GetSyntaxRootAsync().ConfigureAwait(false);
        if (root == null) return null;

        ClassDeclarationSyntax? plugClass = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => c.Identifier.Text == plugClassName);

        if (plugClass != null)
        {
            return (plugClass, document);
        }

        IEnumerable<string> plugReferences = currentProject.Descendants("ItemGroup")
            .Where(x => x.Name == "PlugsReference")
            .Select(x => x.Attribute("Include")!.Value);

        foreach (string reference in plugReferences)
        {
            Project? project = document.Project.Solution.Projects.FirstOrDefault(p => p.OutputFilePath == reference);
            if (project == null) continue;

            Compilation? compilation = await project.GetCompilationAsync().ConfigureAwait(false);
            INamedTypeSymbol? symbol = compilation?.GetTypeByMetadataName(plugClassName);
            if (symbol == null) continue;

            SyntaxReference? syntaxReference = symbol.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxReference != null)
            {
                SyntaxNode syntaxNode = await syntaxReference.GetSyntaxAsync().ConfigureAwait(false);
                return ((ClassDeclarationSyntax)syntaxNode, project.GetDocument(syntaxReference.SyntaxTree)!);
            }
        }

        return null;
    }

    private static async Task<Solution> PlugMethodOnPlug((ClassDeclarationSyntax PlugClass, Document Document) plugInfo, MethodDeclarationSyntax plugMethod)
    {
        DocumentEditor editor = await DocumentEditor.CreateAsync(plugInfo.Document).ConfigureAwait(false);
        editor.InsertAfter(plugInfo.PlugClass.Members.LastOrDefault()!, plugMethod);
        return editor.GetChangedDocument().Project.Solution;
    }

    public static async Task<Solution> MakeClassStatic(Document document, SyntaxNode declaration, CancellationToken c)
    {
        if (declaration is not ClassDeclarationSyntax classDeclaration) return document.Project.Solution;

        classDeclaration = classDeclaration.AddModifiers(SyntaxFactory.Token(SyntaxKind.StaticKeyword));
        DocumentEditor editor = await DocumentEditor.CreateAsync(document, c).ConfigureAwait(false);
        editor.ReplaceNode(declaration, classDeclaration);

        return editor.GetChangedDocument().Project.Solution;
    }
}
