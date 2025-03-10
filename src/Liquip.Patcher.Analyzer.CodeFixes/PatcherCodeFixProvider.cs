using System.Collections.Immutable;
using System.Composition;
using System.Xml.Linq;
using Liquip.Patcher.Analyzer.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using ProjectInfo = Liquip.Patcher.Analyzer.CodeFixes.Models.ProjectInfo;

namespace Liquip.Patcher.Analyzer.CodeFixes
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(PatcherCodeFixProvider)), Shared]
    public class PatcherCodeFixProvider : CodeFixProvider
    {
        public sealed override ImmutableArray<string> FixableDiagnosticIds =>
            DiagnosticMessages.SupportedDiagnostics.Select(d => d.Id).ToImmutableArray();

        public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        private string? _currentPath;
        private ProjectInfo _currentProject;

        private ProjectInfo? LoadCurrentProject(string projectPath)
        {
            if (_currentPath != projectPath)
            {
                _currentPath = projectPath;
                _currentProject = ProjectInfo.From(XDocument.Load(_currentPath));
            }
            return _currentProject;
        }

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            SyntaxNode? root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            if (root == null)
                return;

            foreach (Diagnostic diagnostic in context.Diagnostics)
            {
                if (!diagnostic.Id.StartsWith(PatcherAnalyzer.DiagnosticId)) continue;

                SyntaxNode declaration = root.FindNode(diagnostic.Location.SourceSpan);
                switch (diagnostic.Id)
                {
                    case var id when id == DiagnosticMessages.StaticConstructorTooManyParams.Id:
                        RegisterCodeFix(context, CodeActions.RemoveExtraParametersTitle, _ => CodeActions.RemoveExtraParameters(context.Document, declaration), diagnostic);
                        break;
                    case var id when id == DiagnosticMessages.PlugNotStatic.Id:
                        RegisterCodeFix(context, CodeActions.MakeStaticModifierTitle, c => CodeActions.MakeClassStatic(context.Document, declaration, c), diagnostic);
                        break;

                    case var id when id == DiagnosticMessages.MethodNeedsPlug.Id:
                        RegisterCodeFix(context, CodeActions.PlugMethodTitle, c => CodeActions.PlugMethod(context.Document, declaration, c, diagnostic, LoadCurrentProject(context.Document.Project.FilePath).Value), diagnostic);
                        break;

                    case var id when id == DiagnosticMessages.PlugNameDoesNotMatch.Id:
                        RegisterCodeFix(context, CodeActions.RenamePlugTitle, c => CodeActions.RenamePlug(context.Document, declaration, c, diagnostic), diagnostic);
                        break;

                        // case var id when id == DiagnosticMessages.MethodNotImplemented.Id:
                        //     RegisterCodeFix(context, CodeActions.RemoveMethodTitle, c => CodeActions.RemoveMethod(context.Document, declaration, c, diagnostic), diagnostic);
                        //   break;
                }
            }
        }

        private static void RegisterCodeFix(CodeFixContext context, string title, Func<CancellationToken, Task<Solution>> createChangedSolution, Diagnostic diagnostic) =>
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: title,
                    createChangedSolution: createChangedSolution,
                    equivalenceKey: title),
                diagnostic);

    }

    internal static class CodeActions
    {
        public const string MakeStaticModifierTitle = "Make class static";
        public const string PlugMethodTitle = "Plug method";
        public const string RenamePlugTitle = "Rename plug";
        public const string RemoveExtraParametersTitle = "Remove extra parameters";
        // public const string RemoveMethodTitle = "Remove method";

        public static async Task<Solution> RemoveExtraParameters(Document document, SyntaxNode declaration)
        {
            if (declaration is not MethodDeclarationSyntax methodDeclaration) return document.Project.Solution;

            DocumentEditor editor = await DocumentEditor.CreateAsync(document).ConfigureAwait(false);
            editor.ReplaceNode(methodDeclaration, methodDeclaration.WithParameterList(
                methodDeclaration.ParameterList.WithParameters(
                    SyntaxFactory.SeparatedList(methodDeclaration.ParameterList.Parameters.Where(p => p.Identifier.Text != "aThis")))));
            return editor.GetChangedDocument().Project.Solution;
        }

        public static async Task<Solution> RenamePlug(Document document, SyntaxNode declaration, CancellationToken c, Diagnostic diagnostic)
        {

            if (declaration is not ClassDeclarationSyntax classDeclaration) return document.Project.Solution;

            if (!diagnostic.Properties.TryGetValue("ExpectedName", out string? expectedName)) return document.Project.Solution;

            DocumentEditor editor = await DocumentEditor.CreateAsync(document, c).ConfigureAwait(false);

            editor.SetName(classDeclaration, expectedName);
            return editor.GetChangedDocument().Project.Solution;
        }

        public static async Task<Solution> PlugMethod(Document document, SyntaxNode declaration, CancellationToken c, Diagnostic diagnostic, ProjectInfo currentProject)
        {
            SyntaxNode? root = await document.GetSyntaxRootAsync(c).ConfigureAwait(false);
            if (root == null || !diagnostic.Properties.TryGetValue("MethodName", out string? methodName) || !diagnostic.Properties.TryGetValue("ClassName", out string? className))
                return document.Project.Solution;


            DocumentEditor editor = await DocumentEditor.CreateAsync(document, c).ConfigureAwait(false);
            SyntaxGenerator syntaxGenerator = editor.Generator;

            SyntaxNode plugMethod = syntaxGenerator.MethodDeclaration(methodName, [syntaxGenerator.ParameterDeclaration("aThis", syntaxGenerator.TypeExpression(SpecialType.System_Object), null)], null, null, Accessibility.Public, DeclarationModifiers.Static);

            if (declaration is ClassDeclarationSyntax classDeclaration)
            {
                return await AddMethodToPlug((classDeclaration, document), plugMethod);
            }
            else if (diagnostic.Properties.TryGetValue("PlugClass", out string? plugClassName))
            {
                (ClassDeclarationSyntax PlugClass, Document Document)? plugInfo = await GetPlugClass(plugClassName, document, currentProject);
                if (plugInfo == null) return document.Project.Solution;
                return await AddMethodToPlug(plugInfo.Value, plugMethod);
            }

            BaseNamespaceDeclarationSyntax? namespaceDeclaration = root
                    .ChildNodes()
                    .OfType<BaseNamespaceDeclarationSyntax>()
                    .FirstOrDefault();

            if (namespaceDeclaration == null) return document.Project.Solution;

            SyntaxNode newPlug = syntaxGenerator.ClassDeclaration($"{className}Impl", null, Accessibility.Public, DeclarationModifiers.Static, null, null, [plugMethod]);

            editor.AddMember(namespaceDeclaration, newPlug);
            return editor.GetChangedDocument().Project.Solution;
        }

        private static async Task<(ClassDeclarationSyntax PlugClass, Document Document)?> GetPlugClass(string? plugClassName, Document document, ProjectInfo currentProject)
        {
            if (plugClassName == null) return null;

            SyntaxNode? root = await document.GetSyntaxRootAsync().ConfigureAwait(false);
            if (root == null) return null;

            ClassDeclarationSyntax? plugClass = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .FirstOrDefault(c => c.Identifier.Text == plugClassName);

            if (plugClass != null)
                return (plugClass, document);

            foreach (string reference in currentProject.PlugReferences)
            {
                Project? project = document.Project.Solution.Projects.FirstOrDefault(p => p.OutputFilePath == reference);
                if (project == null) continue;

                Compilation? compilation = await project.GetCompilationAsync().ConfigureAwait(false);
                INamedTypeSymbol? symbol = compilation?.GetTypeByMetadataName(plugClassName);
                if (symbol == null) continue;

                SyntaxReference? syntaxReference = symbol.DeclaringSyntaxReferences.FirstOrDefault();
                if (syntaxReference == null) continue;

                SyntaxNode syntaxNode = await syntaxReference.GetSyntaxAsync().ConfigureAwait(false);
                return ((ClassDeclarationSyntax)syntaxNode, project.GetDocument(syntaxReference.SyntaxTree)!);
            }

            return null;
        }

        private static async Task<Solution> AddMethodToPlug((ClassDeclarationSyntax PlugClass, Document Document) plugInfo, SyntaxNode plugMethod)
        {
            DocumentEditor editor = await DocumentEditor.CreateAsync(plugInfo.Document).ConfigureAwait(false);
            editor.AddMember(plugInfo.PlugClass, plugMethod);
            return editor.GetChangedDocument().Project.Solution;
        }

        public static async Task<Solution> MakeClassStatic(Document document, SyntaxNode declaration, CancellationToken c)
        {
            ClassDeclarationSyntax classDeclaration = (ClassDeclarationSyntax)declaration;
            DocumentEditor editor = await DocumentEditor.CreateAsync(document, c).ConfigureAwait(false);
            editor.SetModifiers(classDeclaration, DeclarationModifiers.Static);

            return editor.GetChangedDocument().Project.Solution;
        }
    }
}
