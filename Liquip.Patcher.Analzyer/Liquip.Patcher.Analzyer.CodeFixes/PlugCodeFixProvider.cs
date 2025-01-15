using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using System.Collections.Immutable;
using System.Composition;

namespace Liquip.Patcher.Analzyer
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(PlugCodeFixProvider)), Shared]
    public class PlugCodeFixProvider : CodeFixProvider
    {

        public const string AddStaticModiferTitle = "Add static modifier";

        public const string PlugMethodTitle = "Plug method";

        public sealed override ImmutableArray<string> FixableDiagnosticIds => DiagnosticMessages.SupportedDiagnostics.Select(d => d.Id).ToImmutableArray();
        public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            SyntaxNode? root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

            // TODO: Replace the following code with your own analysis, generating a CodeAction for each fix to suggest
            foreach (Diagnostic diagnostic in context.Diagnostics.Where(d => d.Id.StartsWith(PatcherAnalyzer.AnalzyerDiagnosticId)))
            {
                TypeDeclarationSyntax declaration = root.FindToken(diagnostic.Location.SourceSpan.Start).Parent.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().First();
                if (diagnostic.Id == DiagnosticMessages.PlugNotStatic.Id)
                {
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            title: AddStaticModiferTitle,
                            createChangedSolution: c => AddStaticModifier(context.Document, declaration, c),
                            equivalenceKey: nameof(AddStaticModiferTitle)),
                        diagnostic);
                }
                else if (diagnostic.Id == DiagnosticMessages.MethodNeedsPlug.Id)
                {
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            title: PlugMethodTitle,
                            createChangedSolution: c => PlugMethod(context.Document, declaration, c, diagnostic),
                            equivalenceKey: nameof(PlugMethodTitle)),
                        diagnostic);
                }
                else if (diagnostic.Id == DiagnosticMessages.PlugNameNeedsImplSuffix.Id)
                {
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            title: "Add 'Impl' suffix",
                            createChangedSolution: c => AddImplSuffix(context.Document, declaration, c),
                            equivalenceKey: "Add Impl suffix"),
                        diagnostic);
                }
            }
        }

        private async Task<Solution> AddStaticModifier(Document document, TypeDeclarationSyntax declaration, CancellationToken c)
        {
            ClassDeclarationSyntax classDeclaration = declaration as ClassDeclarationSyntax;
            classDeclaration = classDeclaration!.AddModifiers(SyntaxFactory.Token(SyntaxKind.StaticKeyword));
            DocumentEditor editor = await DocumentEditor.CreateAsync(document, c).ConfigureAwait(false);

            editor.ReplaceNode(declaration, classDeclaration);
            return editor.GetChangedDocument().Project.Solution;
        }

        private async Task<Solution> AddImplSuffix(Document document, TypeDeclarationSyntax declaration, CancellationToken c)
        {
            ClassDeclarationSyntax classDeclaration = declaration as ClassDeclarationSyntax;
            classDeclaration = classDeclaration!.WithIdentifier(SyntaxFactory.Identifier($"{classDeclaration.Identifier.ValueText}Impl"));
            DocumentEditor editor = await DocumentEditor.CreateAsync(document, c).ConfigureAwait(false);

            editor.ReplaceNode(declaration, classDeclaration);
            return editor.GetChangedDocument().Project.Solution;
        }

        private async Task<Solution> PlugMethod(Document document, TypeDeclarationSyntax declaration, CancellationToken c, Diagnostic diagnostic)
        {

            if (declaration is not ClassDeclarationSyntax @class)
                return document.Project.Solution;

            string methodName = diagnostic.Properties["MethodName"]!;
            MethodDeclarationSyntax method = SyntaxFactory.MethodDeclaration(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ObjectKeyword)), methodName)
                .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
                .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList([SyntaxFactory.Parameter(SyntaxFactory.Identifier("instance")).WithType(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ObjectKeyword)))])))
                .WithBody(SyntaxFactory.Block());

            DocumentEditor editor = await DocumentEditor.CreateAsync(document, c).ConfigureAwait(false);
            editor.InsertMembers(@class, 0, new[] { method });

            return editor.GetChangedDocument().Project.Solution;
        }
    }
}
