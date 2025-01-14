using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Rename;
using System.Collections.Immutable;
using System.Composition;

namespace Liquip.Patcher.Analzyer
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(PlugCodeFixProvider)), Shared]
    public class PlugCodeFixProvider : CodeFixProvider
    {

        public const string MakeClassStaticTitle = "Make class static";

        public const string MakeNewPlugTitle = "Make new plug";

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
                    // Register a code action that will invoke the fix.
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            title: MakeClassStaticTitle,
                            createChangedSolution: c => MakeClassStatic(context.Document, declaration, c),
                            equivalenceKey: nameof(MakeClassStaticTitle)),
                        diagnostic);
                }
                // else if (diagnostic.Id == DiagnosticMessages.MethodNeedsPlug.Id)
                // {
                    // Register a code action that will invoke the fix.
                    // context.RegisterCodeFix(
                    //    CodeAction.Create(
                    //        title: MakeNewPlugTitle,
                    //        createChangedSolution: c => MakeNewPlug(context.Document, declaration, c),
                    //        equivalenceKey: nameof(MakeNewPlugTitle)),
                    //   diagnostic);
                // }
            }
        }

        private async Task<Solution> MakeClassStatic(Document document, TypeDeclarationSyntax declaration, CancellationToken c)
        {
            ClassDeclarationSyntax classDeclaration = declaration as ClassDeclarationSyntax;
            classDeclaration = classDeclaration!.AddModifiers(SyntaxFactory.Token(SyntaxKind.StaticKeyword));
            DocumentEditor editor = await DocumentEditor.CreateAsync(document, c).ConfigureAwait(false);

            editor.ReplaceNode(declaration, classDeclaration);
            return editor.GetChangedDocument().Project.Solution;
        }
    }
}
