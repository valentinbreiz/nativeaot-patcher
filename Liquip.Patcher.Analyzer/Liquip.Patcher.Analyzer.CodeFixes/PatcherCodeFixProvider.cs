using System.Collections.Immutable;
using System.Composition;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace Liquip.Patcher.Analyzer;
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(PatcherCodeFixProvider)), Shared]
public class PatcherCodeFixProvider : CodeFixProvider
{
    public const string AddStaticModifierTitle = "Add static modifier";
    public const string PlugMethodTitle = "Plug method";
    public const string RenamePlugTitle = "Rename plug";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        DiagnosticMessages.SupportedDiagnostics.Select(d => d.Id).ToImmutableArray();

    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    private string _currentPath = string.Empty;
    public XDocument? CurrentProject;

    private readonly Dictionary<string, ClassDeclarationSyntax> _createdPlugs = new();

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
#if DEBUG
        Console.WriteLine("RegisterCodeFixesAsync invoked.");
#endif

        SyntaxNode? root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
        {
#if DEBUG
            Console.WriteLine("Root is null; cannot proceed with code fixes.");
#endif
            return;
        }

#if DEBUG
        Console.WriteLine($"Attempting to load project from: {context.Document.Project.FilePath}");
#endif        
        if (string.Equals(_currentPath, context.Document.Project.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            CurrentProject = XDocument.Load(context.Document.Project.FilePath);
            _currentPath = context.Document.Project.FilePath!;
        }
#if DEBUG
        Console.WriteLine($"Loaded project: {context.Document.Project.FilePath}");
#endif

        foreach (Diagnostic? diagnostic in context.Diagnostics.Where(d => d.Id.StartsWith(PatcherAnalyzer.AnalyzerDiagnosticId)))
        {
            SyntaxNode declaration = root.FindNode(diagnostic.Location.SourceSpan);
#if DEBUG
            Console.WriteLine($"Processing diagnostic: {diagnostic.Id} on node of type: {declaration.GetType()}");
#endif
            switch (diagnostic.Id)
            {
                case var id when id == DiagnosticMessages.PlugNotStatic.Id:
#if DEBUG
                    Console.WriteLine($"Registering code fix for adding static modifier. Diagnostic: {diagnostic.Id}");
#endif
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            title: AddStaticModifierTitle,
                            createChangedSolution: c => AddStaticModifier(context.Document, declaration, c),
                            equivalenceKey: nameof(AddStaticModifierTitle)),
                        diagnostic);
                    break;

                case var id when id == DiagnosticMessages.MethodNeedsPlug.Id:
#if DEBUG
                    Console.WriteLine($"Registering code fix for plugging method. Diagnostic: {diagnostic.Id}");
#endif
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            title: PlugMethodTitle,
                            createChangedSolution: c => PlugMethod(context.Document, declaration, c, diagnostic),
                            equivalenceKey: nameof(PlugMethodTitle)),
                        diagnostic);
                    break;

                case var id when id == DiagnosticMessages.PlugNameDoesNotMatch.Id:
#if DEBUG
                    Console.WriteLine($"Registering code fix for renaming plug. Diagnostic: {diagnostic.Id}");
#endif
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            title: RenamePlugTitle,
                            createChangedSolution: c => RenamePlug(context.Document, declaration, c, diagnostic),
                            equivalenceKey: nameof(RenamePlugTitle)),
                        diagnostic);
                    break;

                default:
#if DEBUG
                    Console.WriteLine($"Unhandled diagnostic ID: {diagnostic.Id}");
#endif
                    break;
            }
        }
    }

    private async Task<Solution> AddStaticModifier(Document document, SyntaxNode declaration, CancellationToken c)
    {
#if DEBUG
        Console.WriteLine("Attempting to add static modifier.");
#endif
        if (declaration is not ClassDeclarationSyntax classDeclaration)
        {
#if DEBUG
            Console.WriteLine("Declaration is not a ClassDeclarationSyntax.");
#endif
            return document.Project.Solution;
        }

#if DEBUG
        Console.WriteLine($"Adding static modifier to class: {classDeclaration.Identifier.Text}");
#endif
        classDeclaration = classDeclaration.AddModifiers(SyntaxFactory.Token(SyntaxKind.StaticKeyword));

        DocumentEditor editor = await DocumentEditor.CreateAsync(document, c).ConfigureAwait(false);
        editor.ReplaceNode(declaration, classDeclaration);

#if DEBUG
        Console.WriteLine($"Static modifier successfully added to class: {classDeclaration.Identifier.Text}");
#endif
        return editor.GetChangedDocument().Project.Solution;
    }

    private async Task<Solution> RenamePlug(Document document, SyntaxNode declaration, CancellationToken c, Diagnostic diagnostic)
    {
#if DEBUG
        Console.WriteLine("Attempting to rename plug.");
#endif
        if (declaration is not ClassDeclarationSyntax classDeclaration)
        {
#if DEBUG
            Console.WriteLine("Declaration is not a ClassDeclarationSyntax.");
#endif
            return document.Project.Solution;
        }

#if DEBUG
        Console.WriteLine($"Processing rename with properties: {string.Join(",", diagnostic.Properties.Select(kv => $"{kv.Key}={kv.Value}"))}");
#endif

        if (!diagnostic.Properties.TryGetValue("ExpectedName", out string? expectedName))
        {
#if DEBUG
            Console.WriteLine("Diagnostic does not contain 'ExpectedName'.");
#endif
            return document.Project.Solution;
        }

#if DEBUG
        Console.WriteLine($"Renaming class from {classDeclaration.Identifier.Text} to {expectedName}.");
#endif
        ClassDeclarationSyntax updatedClassDeclaration = classDeclaration.WithIdentifier(SyntaxFactory.Identifier(expectedName!));

        DocumentEditor editor = await DocumentEditor.CreateAsync(document, c).ConfigureAwait(false);
        editor.ReplaceNode(classDeclaration, updatedClassDeclaration);

#if DEBUG
        Console.WriteLine($"Class renamed to {expectedName}.");
#endif
        return editor.GetChangedDocument().Project.Solution;
    }

    private async Task<Solution> PlugMethod(Document document, SyntaxNode declaration, CancellationToken c, Diagnostic diagnostic)
    {
#if DEBUG
        Console.WriteLine("Attempting to plug method.");
#endif

        SyntaxNode? root = await document.GetSyntaxRootAsync(c).ConfigureAwait(false);
        if (root == null)
        {
#if DEBUG
            Console.WriteLine("Root is null; cannot proceed with PlugMethod.");
#endif
            return document.Project.Solution;
        }

        if (!diagnostic.Properties.TryGetValue("MethodName", out string? methodName) ||
              !diagnostic.Properties.TryGetValue("ClassName", out string? className))
        {
#if DEBUG
            Console.WriteLine("Diagnostic properties are missing.");
#endif
            return document.Project.Solution;
        }

        MethodDeclarationSyntax plugMethod = CreatePlugMethod(methodName!);

#if DEBUG
        Console.WriteLine($"Creating plug method '{methodName}'.");
#endif

        if (declaration is ClassDeclarationSyntax classDeclaration)
        {
#if DEBUG
            Console.WriteLine($"Plugging method '{methodName}' into existing plug class '{className}'.");
#endif
            return await PlugMethodOnPlug((classDeclaration, document), plugMethod);
        }
        else
        {
            DocumentEditor editor = await DocumentEditor.CreateAsync(document, c).ConfigureAwait(false);
            BaseNamespaceDeclarationSyntax? namespaceDeclaration = root
            .ChildNodes()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .FirstOrDefault();

            if (namespaceDeclaration == null) return document.Project.Solution;
#if DEBUG
            Console.WriteLine($"Plugging method '{methodName}' into class '{className}'.");
#endif
            if (diagnostic.Properties.TryGetValue("PlugClass", out string? plugClassName))
            {
                (ClassDeclarationSyntax PlugClass, Document Document)? plugInfo = await GetPlugClass(plugClassName, document);
                if (plugInfo == null) return document.Project.Solution;
                return await PlugMethodOnPlug(plugInfo.Value, plugMethod);
            }

            ClassDeclarationSyntax newPlug = SyntaxFactory.ClassDeclaration($"{className}Impl")
                .AddAttributeLists(SyntaxFactory.AttributeList(
                                        SyntaxFactory.SingletonSeparatedList(
                                                        SyntaxFactory.Attribute(SyntaxFactory.ParseName("Plug")).AddArgumentListArguments(
                                                            SyntaxFactory.AttributeArgument(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal($"{namespaceDeclaration.Name}.{className}")))
                                                                                                                                            )
                                                                            )
                                                                                    ))
                .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.StaticKeyword))
                .AddMembers(plugMethod);

            editor.ReplaceNode(namespaceDeclaration, namespaceDeclaration.AddMembers(newPlug));

            return editor.GetChangedDocument().Project.Solution;
        }
    }

    private MethodDeclarationSyntax CreatePlugMethod(string name)
    {
#if DEBUG
        Console.WriteLine($"Creating a new plug method: {name}");
#endif
        return SyntaxFactory.MethodDeclaration(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)), name)
            .WithBody(SyntaxFactory.Block())
            .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.Parameter(SyntaxFactory.ParseToken("instance"))
                    .WithType(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ObjectKeyword))))))
            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.StaticKeyword));
    }

    private async Task<(ClassDeclarationSyntax PlugClass, Document Document)?> GetPlugClass(string? plugClassName, Document document)
    {
#if DEBUG
        Console.WriteLine("GetPlugClass invoked.");
#endif

        if (plugClassName == null)
        {
#if DEBUG
            Console.WriteLine("plugClassName is null. Returning null.");
#endif
            return null;
        }

#if DEBUG
        Console.WriteLine($"Searching for class: {plugClassName} in the document...");
#endif
        SyntaxNode? root = await document.GetSyntaxRootAsync().ConfigureAwait(false);
        if (root == null)
        {
#if DEBUG
            Console.WriteLine("Root is null, cannot search for plug class.");
#endif
            return null;
        }

        var plugClass = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => c.Identifier.Text == plugClassName);

        if (plugClass != null)
        {
#if DEBUG
            Console.WriteLine($"Found plug class: {plugClass.Identifier.Text}");
#endif
            return (plugClass, document);
        }

        IEnumerable<string> plugReferences = CurrentProject.Descendants("ItemGroup").Where(x => x.Name == "PlugsReference").Select(x => x.Attribute("Include").Value);
        foreach (string reference in plugReferences)
        {
            Project? project = document.Project.Solution.Projects.FirstOrDefault(p => p.OutputFilePath == reference);
            if (project == null) continue;

            Compilation compilation = (await project.GetCompilationAsync())!;
            ISymbol? symbol = compilation.GetTypeByMetadataName(plugClassName);
            if (symbol == null) continue;
            
            SyntaxReference syntaxReference = symbol.DeclaringSyntaxReferences.FirstOrDefault()!;
            return ((ClassDeclarationSyntax)(await syntaxReference.GetSyntaxAsync()), project.GetDocument(syntaxReference.SyntaxTree)!);
        }

#if DEBUG
        Console.WriteLine($"Plug class '{plugClassName}' not found in plug references.");
#endif
        return null;
    }

    private async Task<Solution> PlugMethodOnPlug((ClassDeclarationSyntax PlugClass, Document Document) plugInfo, MethodDeclarationSyntax plugMethod)
    {
        DocumentEditor editor = await DocumentEditor.CreateAsync(plugInfo.Document).ConfigureAwait(false);

#if DEBUG
        Console.WriteLine($"Plugging method into class: {plugInfo.PlugClass.Identifier.Text}");
#endif
        editor.InsertAfter(plugInfo.PlugClass.Members.LastOrDefault()!, plugMethod);
        return editor.GetChangedDocument().Project.Solution;
    }
}
