using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace Liquip.Patcher.Analzyer
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(PatcherCodeFixProvider)), Shared]
    public class PatcherCodeFixProvider : CodeFixProvider
    {

        public const string AddStaticModiferTitle = "Add static modifier";
        public const string PlugMethodTitle = "Plug method";
        public const string RenamePlugTitle = "Rename plug";

        public sealed override ImmutableArray<string> FixableDiagnosticIds => DiagnosticMessages.SupportedDiagnostics.Select(d => d.Id).ToImmutableArray();
        public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            if (root == null) return;

            foreach (Diagnostic diagnostic in context.Diagnostics.Where(d => d.Id.StartsWith(PatcherAnalyzer.AnalzyerDiagnosticId)))
            {
                SyntaxNode declaration = root.FindNode(diagnostic.Location.SourceSpan);

                switch (diagnostic.Id)
                {
                    case var id when id == DiagnosticMessages.PlugNotStatic.Id:
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                title: AddStaticModiferTitle,
                                createChangedSolution: c => AddStaticModifier(context.Document, declaration, c),
                                equivalenceKey: nameof(AddStaticModiferTitle)),
                            diagnostic);
                        break;

                    case var id when id == DiagnosticMessages.MethodNeedsPlug.Id:
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                title: PlugMethodTitle,
                                createChangedSolution: c => PlugMethod(context.Document, declaration, c, diagnostic),
                                equivalenceKey: nameof(PlugMethodTitle)),
                            diagnostic);
                        break;

                    case var id when id == DiagnosticMessages.PlugNameDoesNotMatch.Id:
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                title: RenamePlugTitle,
                                createChangedSolution: c => RenamePlug(context.Document, declaration, c, diagnostic),
                                equivalenceKey: nameof(RenamePlugTitle)),
                            diagnostic);
                        break;
                }
            }
        }


        private async Task<Solution> AddStaticModifier(Document document, SyntaxNode declaration, CancellationToken c)
        {
            ClassDeclarationSyntax classDeclaration = declaration as ClassDeclarationSyntax;
            classDeclaration = classDeclaration!.AddModifiers(SyntaxFactory.Token(SyntaxKind.StaticKeyword));
            DocumentEditor editor = await DocumentEditor.CreateAsync(document, c).ConfigureAwait(false);

            editor.ReplaceNode(declaration, classDeclaration);
            return editor.GetChangedDocument().Project.Solution;
        }

        private async Task<Solution> RenamePlug(Document document, SyntaxNode declaration, CancellationToken c, Diagnostic diagnostic)
        {
            if (declaration is not ClassDeclarationSyntax classDeclaration)
            {
                throw new InvalidOperationException("Expected a ClassDeclarationSyntax");
            }

            if (!diagnostic.Properties.TryGetValue("ExpectedName", out string? expectedName))
            {
                throw new InvalidOperationException("Diagnostic does not contain the expected class name");
            }

            var updatedClassDeclaration = classDeclaration.WithIdentifier(SyntaxFactory.Identifier(expectedName));

            DocumentEditor editor = await DocumentEditor.CreateAsync(document, c).ConfigureAwait(false);
            editor.ReplaceNode(classDeclaration, updatedClassDeclaration);

            return editor.GetChangedDocument().Project.Solution;
        }



        private async Task<Solution> PlugMethod(Document document, SyntaxNode declaration, CancellationToken c, Diagnostic diagnostic)
        {
            SyntaxNode? root = await document.GetSyntaxRootAsync(c).ConfigureAwait(false);
            if (root == null) return document.Project.Solution;

            DocumentEditor editor = await DocumentEditor.CreateAsync(document, c).ConfigureAwait(false);
            string methodName = diagnostic.Properties["MethodName"];
            string? pluggedClassName = diagnostic.Properties["PluggedClass"];
            string className = diagnostic.Properties["ClassName"];

            if (declaration is not AttributeSyntax && declaration is not MemberAccessExpressionSyntax)
                return document.Project.Solution;

            if (string.IsNullOrEmpty(pluggedClassName))
            {
                if (root is CompilationUnitSyntax compilationUnit && !compilationUnit.Usings.Any(u => u.Name.ToString() == "Liquip.API.Attributes"))
                {
                    editor.ReplaceNode(
                        compilationUnit,
                        compilationUnit.AddUsings(SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("Liquip.API.Attributes"))));
                }
                if (declaration is not MemberAccessExpressionSyntax memberAccess) return document.Project.Solution;

                var plugAttribute = SyntaxFactory.Attribute(
                SyntaxFactory.IdentifierName("Plug"),
                SyntaxFactory.AttributeArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.AttributeArgument(
                            SyntaxFactory.TypeOfExpression(
                                SyntaxFactory.IdentifierName(memberAccess.Expression.ToString()))))));
                                
                var plugMethod = CreatePlugMethod(methodName);
                var newPlugClass = CreatePlugClass(className, plugAttribute, plugMethod);

                editor.InsertAfter(declaration, newPlugClass);
            }
            else
            {
                var plugMethod = CreatePlugMethod(methodName);
                var plugClass = GetPlugClass(className, await document.Project.GetCompilationAsync(c));
                editor.InsertMembers(plugClass, 0, new[] { plugMethod });
            }

            return editor.GetChangedDocument().Project.Solution;
        }


        private MethodDeclarationSyntax CreatePlugMethod(string methodName)
        {
            return SyntaxFactory.MethodDeclaration(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)),
                methodName)
                .WithModifiers(SyntaxFactory.TokenList(
                    SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                    SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
                .WithParameterList(
                    SyntaxFactory.ParameterList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.Parameter(SyntaxFactory.Identifier("instance"))
                                .WithType(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ObjectKeyword))))))
                .WithBody(SyntaxFactory.Block());
        }

        private ClassDeclarationSyntax CreatePlugClass(string className, AttributeSyntax plugAttribute, MethodDeclarationSyntax plugMethod)
        {
            return SyntaxFactory.ClassDeclaration($"{className}Impl")
                .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
                .WithAttributeLists(SyntaxFactory.SingletonList(SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(plugAttribute))))
                .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(plugMethod));
        }

        public static SyntaxNode GetPlugClass(string className, Compilation compilation)
        {
            // TODO: Get Plug from PlugReferences Assembly in .csproj 
            ISymbol? symbol = compilation.GetTypeByMetadataName(className);
            if (symbol == null) return null;
            return symbol.DeclaringSyntaxReferences[0].GetSyntax();
        }
    }
}