using System.Collections.Immutable;
using System.Composition;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;

namespace Liquip.Patcher.Analyzer
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(PatcherCodeFixProvider)), Shared]
    public class PatcherCodeFixProvider : CodeFixProvider
    {
        public const string MakeStaticModifierTitle = "Make class static";
        public const string PlugMethodTitle = "Plug method";
        public const string RenamePlugTitle = "Rename plug";
        public const string RemoveExtraParametersTitle = "Remove extra parameters";


        public sealed override ImmutableArray<string> FixableDiagnosticIds =>
            DiagnosticMessages.SupportedDiagnostics.Select(d => d.Id).ToImmutableArray();

        public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        private string _currentPath = string.Empty;
        private XDocument? CurrentProject { get; set; }

        private XDocument? LoadCurrentProject()
        {
            if (!string.IsNullOrEmpty(_currentPath))
                return XDocument.Load(_currentPath);

            return null;
        }

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            SyntaxNode? root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            if (root == null)
                return;


            if (_currentPath != context.Document.Project.FilePath)
            {
                _currentPath = context.Document.Project.FilePath!;
                CurrentProject = LoadCurrentProject();
            }

            foreach (Diagnostic diagnostic in context.Diagnostics)
            {
                SyntaxNode declaration = root.FindNode(diagnostic.Location.SourceSpan);

                switch (diagnostic.Id)
                {
                    case var id when id == DiagnosticMessages.StaticConstructorContainsParameters.Id:
                        RegisterCodeFix(context, RemoveExtraParametersTitle, c => CodeActions.RemoveExtraParameters(context.Document, declaration), diagnostic);
                        break;
                    case var id when id == DiagnosticMessages.PlugNotStatic.Id:
                        RegisterCodeFix(context, MakeStaticModifierTitle, c => CodeActions.MakeClassStatic(context.Document, declaration, c), diagnostic);
                        break;

                    case var id when id == DiagnosticMessages.MethodNeedsPlug.Id:
                        RegisterCodeFix(context, PlugMethodTitle, c => CodeActions.PlugMethod(context.Document, declaration, c, diagnostic, CurrentProject!), diagnostic);
                        break;

                    case var id when id == DiagnosticMessages.PlugNameDoesNotMatch.Id:
                        RegisterCodeFix(context, RenamePlugTitle, c => CodeActions.RenamePlug(context.Document, declaration, c, diagnostic), diagnostic);
                        break;
                }
            }
        }

        private static void RegisterCodeFix(CodeFixContext context, string title, Func<CancellationToken, Task<Solution>> createChangedSolution, Diagnostic diagnostic)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: title,
                    createChangedSolution: createChangedSolution,
                    equivalenceKey: title),
                diagnostic);
        }
    }

}
