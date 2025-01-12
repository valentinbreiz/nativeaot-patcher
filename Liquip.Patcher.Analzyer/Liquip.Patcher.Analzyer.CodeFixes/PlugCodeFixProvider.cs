using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Rename;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Liquip.Patcher.Analzyer
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(PlugCodeFixProvider)), Shared]
    public class PlugCodeFixProvider : CodeFixProvider
    {
        private static string CodeFixTitle = "Convert to upper";

        public sealed override ImmutableArray<string> FixableDiagnosticIds
        {
            get { return ImmutableArray.Create(PlugAnalyzer.DiagnosticId); }
        }

        public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

            // TODO: Replace the following code with your own analysis, generating a CodeAction for each fix to suggest
            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;

            // Find the type declaration identified by the diagnostic.
            var declaration = root.FindToken(diagnosticSpan.Start).Parent.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().First();

            // Register a code action that will invoke the fix.
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: CodeFixTitle,
                    createChangedSolution: c => MakeUppercaseAsync(context.Document, declaration, c),
                    equivalenceKey: nameof(CodeFixTitle)),
                diagnostic);
        }

        private async Task<Solution> MakeUppercaseAsync(Document document, TypeDeclarationSyntax typeDecl, CancellationToken cancellationToken)
        {
            // Compute new uppercase name.
            var identifierToken = typeDecl.Identifier;
            var newName = identifierToken.Text.ToUpperInvariant();

            // Get the symbol representing the type to be renamed.
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl, cancellationToken);

            // Produce a new solution that has all references to that type renamed, including the declaration.
            var originalSolution = document.Project.Solution;
            var optionSet = originalSolution.Workspace.Options;
            var newSolution = await Renamer.RenameSymbolAsync(document.Project.Solution, typeSymbol, new SymbolRenameOptions(), newName, cancellationToken).ConfigureAwait(false);

            // Return the new solution with the now-uppercase type name.
            return newSolution;
        }

        // Example of modifying a document adding an attribute 

        //private async Task<Document> AddAttributeToClass(Document document, TypeDeclarationSyntax typeDeclaration,
        //    CancellationToken cancellationToken)
        //{
        //    var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        //    editor.ReplaceNode(typeDeclaration, AddTraitAttribute(typeDeclaration));
        //    return editor.GetChangedDocument();
        //}

        //private TypeDeclarationSyntax AddTraitAttribute(TypeDeclarationSyntax source)
        //{
        //    var attributeListSyntax = AttributeList(
        //        SingletonSeparatedList<AttributeSyntax>(
        //            Attribute(IdentifierName("Trait"))));

        //    return source.AddAttributeLists(
        //        attributeListSyntax);
        //}
    }
}
