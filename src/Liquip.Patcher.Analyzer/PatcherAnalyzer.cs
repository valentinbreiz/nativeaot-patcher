using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Liquip.Patcher.Analyzer.Extensions;
using Liquip.Patcher.Analyzer.Models;
using Liquip.Patcher.Analyzer.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Liquip.Patcher.Analyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class PatcherAnalyzer : DiagnosticAnalyzer
{
    public const string AnalyzerDiagnosticId = "NAOT";
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => DiagnosticMessages.SupportedDiagnostics;

    private readonly ConcurrentDictionary<string, PlugInfo> _pluggedClasses = new();

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction((CompilationStartAnalysisContext compilationContext) =>
        {
            AnalyzeCompilation(compilationContext);
            compilationContext.RegisterSyntaxNodeAction(AnalyzePlugAttribute, SyntaxKind.Attribute);
            compilationContext.RegisterSyntaxNodeAction(AnalyzeAccessedMember, SyntaxKind.SimpleMemberAccessExpression);
            compilationContext.RegisterSyntaxTreeAction(AnalyzeSyntaxTree);
        });
    }

    private void AnalyzeCompilation(CompilationStartAnalysisContext context)
    {
        foreach (KeyValuePair<string, PlugInfo> pair in _pluggedClasses)
        {
            if (!context.Compilation.ContainsSymbolsWithName(pair.Key, SymbolFilter.Type))
            {
                _pluggedClasses.TryRemove(pair.Key, out PlugInfo _);
            }
        }
    }

    private void AnalyzeSyntaxTree(SyntaxTreeAnalysisContext context)
    {
        SyntaxNode syntaxRoot = context.Tree.GetRoot(context.CancellationToken);
        foreach (KeyValuePair<string, PlugInfo> pair in _pluggedClasses)
        {
            if (!syntaxRoot.TryFindNode(pair.Value.Plug.Span, out ClassDeclarationSyntax? node))
            {
                _pluggedClasses.TryRemove(pair.Key, out PlugInfo _);
            }
        }
    }

    private void AnalyzeAccessedMember(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not MemberAccessExpressionSyntax elementAccessExpressionSyntax)
            return;

        ISymbol? symbol = context.SemanticModel.GetSymbolInfo(elementAccessExpressionSyntax.Expression).Symbol;
        if (symbol is not INamespaceOrTypeSymbol classSymbol)
            return;

        if (context.SemanticModel.GetSymbolInfo(elementAccessExpressionSyntax).Symbol is not IMethodSymbol accessedMethod)
            return;

        ImmutableDictionary<string, string?> defaultProperties = ImmutableDictionary.CreateRange(new[]
        {
            new KeyValuePair<string, string?>("MethodName", accessedMethod.Name),
            new KeyValuePair<string, string?>("ClassName", classSymbol.Name)
        });

        if (_pluggedClasses.TryGetValue(classSymbol.Name, out PlugInfo plugInfo))
        {
            ClassDeclarationSyntax plugClass = plugInfo.Plug;
            if (!plugClass.TryGetMemberByName<MethodDeclarationSyntax>(accessedMethod.Name, out MethodDeclarationSyntax? _))
            {
                if (CheckIfNeedsPlug(accessedMethod, plugClass))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticMessages.MethodNeedsPlug,
                        elementAccessExpressionSyntax.Expression.GetLocation(),
                        ImmutableDictionary.CreateRange(new[]
                        {
                            new KeyValuePair<string, string?>("PlugClass", plugClass.Identifier.Text)
                        }),
                        accessedMethod.Name,
                        classSymbol.Name
                    ));
                }
            }
        }
        else
        {
            foreach (ISymbol? member in classSymbol.GetMembers())
            {
                if (member is not IMethodSymbol methodSymbol) continue;
                if (methodSymbol.MethodKind == MethodKind.Ordinary && CheckIfNeedsPlug(methodSymbol, null))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticMessages.MethodNeedsPlug,
                        elementAccessExpressionSyntax.Expression.GetLocation(),
                        defaultProperties,
                        methodSymbol.Name,
                        classSymbol.Name
                    ));
                }
            }
        }
    }

    private void AnalyzePlugAttribute(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not AttributeSyntax attribute || attribute.Name.ToString() != "Plug")
            return;

        ClassDeclarationSyntax? plugClass = attribute.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        if (plugClass == null) return;

        if (!(attribute.GetAttributeValue("TargetName", context, out string? targetName) ||
              attribute.GetAttributeValue(0, context, out targetName)))
        {
            if (!(attribute.GetAttributeValue("Target", context, out string? type) ||
                  attribute.GetAttributeValue(0, context, out type)))
            {
                return;

            }

            targetName = type;
        }

        if (string.IsNullOrEmpty(targetName)) return;

        string assemblyName = context.Compilation.AssemblyName ?? string.Empty;
        string typeName = targetName;

        if (targetName.Contains(','))
        {
            string[] statement = targetName.Split(',');
            typeName = statement[0].Trim();
            assemblyName = statement.Last().Trim();
        }

        INamedTypeSymbol? symbol = context.Compilation.GetTypeByMetadataName(typeName);
        bool existInAssembly = symbol != null ||
            context.Compilation.ExternalReferences.Any(x => x.Display != null && x.Display == assemblyName);

        if (!existInAssembly && !attribute.GetAttributeValue<bool>("IsOptional", context))
        {
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticMessages.TypeNotFound, attribute.GetLocation(), targetName));
            return;
        }

        if (symbol == null) return;

        AnalyzePlugClass(plugClass, symbol.Name, context);
        AnalyzePluggedClass(symbol, plugClass, context);
    }

    private void AnalyzePlugClass(ClassDeclarationSyntax classDeclarationSyntax, string pluggedClassName, SyntaxNodeAnalysisContext context)
    {
        if (!classDeclarationSyntax.Modifiers.Any(SyntaxKind.StaticKeyword))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticMessages.PlugNotStatic,
                classDeclarationSyntax.GetLocation(),
                classDeclarationSyntax.Identifier.Text));
        }

        if (!string.IsNullOrEmpty(pluggedClassName))
        {
            string expectedName = $"{pluggedClassName}Impl";
            if (classDeclarationSyntax.Identifier.Text != expectedName)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticMessages.PlugNameDoesNotMatch,
                    classDeclarationSyntax.GetLocation(),
                    ImmutableDictionary.CreateRange(new[] {
                        new KeyValuePair<string, string?>("ExpectedName", expectedName)
                    }),
                    classDeclarationSyntax.Identifier.Text,
                    expectedName));
            }
        }
    }

    private void AnalyzePluggedClass(INamedTypeSymbol? symbol, ClassDeclarationSyntax? plugClass, SyntaxNodeAnalysisContext context)
    {
        if (plugClass == null || symbol == null) return;

        _pluggedClasses[symbol.Name] = new PlugInfo(false, plugClass);
        AnalyzePluggedClassMembers(plugClass, symbol, context);
    }

    private void AnalyzePluggedClassMembers(ClassDeclarationSyntax plugClass, INamedTypeSymbol symbol, SyntaxNodeAnalysisContext context)
    {
        IEnumerable<IMethodSymbol> methods = symbol.GetMembers().OfType<IMethodSymbol>().Where(m => m.MethodKind == MethodKind.Ordinary);
        int unpluggedMethods = 0;
        PlugInfo entry = _pluggedClasses[symbol.Name];

        if (entry.MethodsNeedPlug)
        {
            foreach (IMethodSymbol method in methods)
            {
                if (CheckIfNeedsPlug(method, plugClass))
                {
                    unpluggedMethods++;
                    ImmutableDictionary<string, string?> diagnosticProperties = ImmutableDictionary.CreateRange(new[]
                    {
                        new KeyValuePair<string, string?>("ClassName", plugClass.Identifier.Text),
                        new KeyValuePair<string, string?>("MethodName", method.Name)
                    });

                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticMessages.MethodNeedsPlug,
                        plugClass.GetLocation(),
                        diagnosticProperties,
                        method.Name,
                        symbol.Name));
                }
            }
        }

        _pluggedClasses[symbol.Name] = entry with { MethodsNeedPlug = unpluggedMethods > 0 };
        AnalyzePluggedClassCtors(plugClass, symbol, methods, context);

        foreach (MethodDeclarationSyntax unimplemented in plugClass.Members.OfType<MethodDeclarationSyntax>()
                 .Where(method => !methods.Any(x => x.Name == method.Identifier.Text)))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticMessages.MethodNotImplemented,
                plugClass.GetLocation(),
                unimplemented.Identifier.Text,
                plugClass.Identifier.Text));
        }
    }

    private void AnalyzePluggedClassCtors(ClassDeclarationSyntax plugClass, INamedTypeSymbol symbol,
        IEnumerable<IMethodSymbol> methods, SyntaxNodeAnalysisContext context)
    {
        if (plugClass.TryGetMemberByName("CCtor", out MethodDeclarationSyntax? cctor))
        {
            if (cctor.ParameterList.Parameters.Count > 1 ||
                !cctor.ParameterList.Parameters.Any(param => param.Identifier.Text == "aThis"))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticMessages.StaticConstructorContainsParameters,
                    cctor.GetLocation(),
                    cctor.Identifier.Text));
            }

            if (!methods.Any(x => x.IsStatic && x.Name == ".cctor"))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticMessages.MethodNotImplemented,
                    cctor.GetLocation(),
                    ".cctor",
                    symbol.Name));
            }
        }
    }

    private static bool CheckIfNeedsPlug(IMethodSymbol methodSymbol, ClassDeclarationSyntax? plugClass) =>
        plugClass?.TryGetMemberByName(methodSymbol.Name, out MethodDeclarationSyntax? _) != true &&
        methodSymbol.GetAttributes().Any(x =>
            (x.AttributeClass?.Name == "MethodImplAttribute" &&
             x.GetAttributeValue<MethodImplOptions>(0).HasFlag(MethodImplOptions.InternalCall)) ||
            x.AttributeClass?.Name == "DllImportAttribute" ||
            x.AttributeClass?.Name == "LibraryImportAttribute");
}
