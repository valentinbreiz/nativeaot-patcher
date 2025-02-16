using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Liquip.Patcher.Analyzer.Extensions;
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

    private readonly ConcurrentDictionary<string, ClassDeclarationSyntax> _pluggedClasses = [];

    public override void Initialize(AnalysisContext context)
    {
        DebugLogger.Log("Initializing...");
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction((context) =>
        {
            _pluggedClasses.Clear();

            context.RegisterSyntaxNodeAction(AnalyzePlugAttribute, SyntaxKind.Attribute);
            context.RegisterSyntaxNodeAction(AnalyzeAccessedMember, SyntaxKind.SimpleMemberAccessExpression);
            context.RegisterSyntaxTreeAction(AnalyzeSyntaxTree); // Check if the plugged class no longer exists in the syntaxTree
        });
    }

    private void AnalyzeSyntaxTree(SyntaxTreeAnalysisContext context)
    {
        SyntaxNode syntaxRoot = context.Tree.GetRoot(context.CancellationToken);
        foreach (KeyValuePair<string, ClassDeclarationSyntax> pair in _pluggedClasses)
        {
            if (syntaxRoot.TryFindNode(pair.Value.Span, out ClassDeclarationSyntax? node) && node is null)
                _pluggedClasses.TryRemove(pair.Key, out _);
        }
    }

    private void AnalyzeAccessedMember(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not MemberAccessExpressionSyntax elementAccessExpressionSyntax)
            return;

        DebugLogger.Log($"Identifier is: {elementAccessExpressionSyntax.Expression}");

        ISymbol? symbol = context.SemanticModel.GetSymbolInfo(elementAccessExpressionSyntax.Expression).Symbol;
        if (symbol is not INamespaceOrTypeSymbol classSymbol || context.SemanticModel.GetSymbolInfo(elementAccessExpressionSyntax).Symbol is not IMethodSymbol accessedMethod)
            return;

        ImmutableDictionary<string, string?> defaultProperties = ImmutableDictionary.CreateRange([
            new KeyValuePair<string, string?>("MethodName", accessedMethod.Name),
            new KeyValuePair<string, string?>("ClassName", classSymbol.Name)
        ]);

        if (_pluggedClasses.TryGetValue(classSymbol.Name, out ClassDeclarationSyntax plugClass))
        {
            DebugLogger.Log($"Found plugged class: {plugClass.Identifier.Text}.");


            if (!plugClass.TryGetMemberByName<MethodDeclarationSyntax>(accessedMethod.Name, out _) && CheckIfNeedsPlug(accessedMethod, plugClass))
            {
                DebugLogger.Log($"Method {accessedMethod.Name} does not exist in the plugged class {plugClass.Identifier.Text}.");
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
        else
        {
            foreach (ISymbol? member in classSymbol.GetMembers())
            {
                if (member is not IMethodSymbol methodSymbol || (methodSymbol.MethodKind == MethodKind.Ordinary && CheckIfNeedsPlug(methodSymbol, plugClass)))
                    continue;

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

    private void AnalyzePlugAttribute(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not AttributeSyntax attribute || attribute.Name.ToString() != "Plug")
            return;

        ClassDeclarationSyntax plugClass = attribute
        .Ancestors()
        .OfType<ClassDeclarationSyntax>()
        .FirstOrDefault();

        if (plugClass == null)
            return;

        DebugLogger.Log($"Found Plug attribute. Attribute: {attribute}");

        // Get the target name from the attribute
        string? targetName = string.Empty;
        if (!(attribute.GetAttributeValue("TargetName", context, out targetName) || attribute.GetAttributeValue(0, context, out targetName)))
        {
            if (!(attribute.GetAttributeValue("Target", context, out string? type) || attribute.GetAttributeValue(0, context, out type))) // Trying to get a 'Type' value only returns the name of the type
            {
                DebugLogger.Log("No Type");
                return;
            }
            //  DebugLogger.Log(type?.FullName);

            targetName = type;
        }


        DebugLogger.Log($"IsEmpty: {string.IsNullOrEmpty(targetName)}, TargetName:{targetName}");

        if (string.IsNullOrEmpty(targetName))
            return;

        DebugLogger.Log($"Target Name: {targetName}");

        string assemblyName = context.Compilation.AssemblyName ?? string.Empty;
        string typeName = targetName!;

        DebugLogger.Log($"Assembly Name: {assemblyName}");

        if (targetName.Contains(','))
        {
            string[] statement = targetName!.Split(',');
            assemblyName = statement.Last().Trim();
            typeName = statement[0];
        }

        DebugLogger.Log($"After targetName, typeName:{typeName}");

        INamedTypeSymbol? symbol = context.Compilation.GetTypeByMetadataName(typeName);

        // DebugLogger.Log($"Typeof name is:{symbol.Name}");
        bool existInAssembly = symbol != null || context.Compilation.ExternalReferences
            .Any(x => x.Display != null && x.Display == assemblyName);

        if (!existInAssembly && !attribute.GetAttributeValue<bool>("IsOptional", context))
        {
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticMessages.TypeNotFound, attribute.GetLocation(), targetName));
            return;
        }

        AnalyzePlugClass(plugClass, symbol!.Name, context);
        AnalyzePluggedClass(symbol, plugClass, context);
    }

    private void AnalyzePlugClass(ClassDeclarationSyntax classDeclarationSyntax, string pluggedClassName, SyntaxNodeAnalysisContext context)
    {
        if (!classDeclarationSyntax.Modifiers.Any(SyntaxKind.StaticKeyword))
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticMessages.PlugNotStatic, classDeclarationSyntax.GetLocation(), classDeclarationSyntax.Identifier.Text));

        if (!string.IsNullOrEmpty(pluggedClassName))
        {
            string expectedName = $"{pluggedClassName}Impl";
            if (classDeclarationSyntax.Identifier.Text != expectedName)
            {
                context.ReportDiagnostic(Diagnostic.Create(DiagnosticMessages.PlugNameDoesNotMatch, classDeclarationSyntax.GetLocation(),
                    ImmutableDictionary.CreateRange(new[] { new KeyValuePair<string, string?>("ExpectedName", expectedName) }),
                    classDeclarationSyntax.Identifier.Text, expectedName));
            }
        }
    }

    private void AnalyzePluggedClass(INamedTypeSymbol? symbol, ClassDeclarationSyntax? plugClass, SyntaxNodeAnalysisContext context)
    {
        if (plugClass == null || symbol == null) return;

        _pluggedClasses[symbol.Name] = plugClass;

        IEnumerable<IMethodSymbol> methods = symbol.GetMembers().OfType<IMethodSymbol>().Where(m => m.MethodKind == MethodKind.Ordinary);

        foreach (IMethodSymbol method in methods)
        {
            if (CheckIfNeedsPlug(method, plugClass))
            {
                var diagnosticProperties = ImmutableDictionary.CreateRange(new[]
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

        if (plugClass.TryGetMemberByName("CCtor", out MethodDeclarationSyntax cctor))
        {
            DebugLogger.Log($"Static Constructor Analysis: Method='{cctor.Identifier}', " +
                               $"ParameterCount={cctor.ParameterList.Parameters.Count}, " +
                               $"HasAThis={cctor.ParameterList.Parameters.Any(param => param.Identifier.Text == "aThis")}, " +
                               $"Class='{plugClass.Identifier.Text}'");

            if (cctor.ParameterList.Parameters.Count > 1 || !cctor.ParameterList.Parameters.Any(param => param.Identifier.Text == "aThis"))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                       DiagnosticMessages.StaticConstructorContainsParameters,
                       cctor.GetLocation(),
                       cctor.Identifier.Text
                    ));
            }

            if (!methods.Any(x => x.IsStatic && x.Name == ".cctor"))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                   DiagnosticMessages.MethodNotImplemented,
                   cctor.GetLocation(),
                   ".cctor",
                   symbol.Name
                ));
            }
        }

        foreach (MethodDeclarationSyntax unimplemented in plugClass.Members.OfType<MethodDeclarationSyntax>().Where(method => !methods.Any(x => x.Name == method.Identifier.Text)))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticMessages.MethodNotImplemented,
                plugClass.GetLocation(),
                unimplemented.Identifier.Text,
                plugClass.Identifier.Text));
        }
    }



    private static bool CheckIfNeedsPlug(IMethodSymbol methodSymbol, ClassDeclarationSyntax plugClass) => plugClass?.TryGetMemberByName(methodSymbol.Name, out MethodDeclarationSyntax _) != true && methodSymbol.GetAttributes().Any(x => (x.AttributeClass?.Name == "MethodImplAttribute" && x.GetAttributeValue<MethodImplOptions>(0).HasFlag(MethodImplOptions.InternalCall)) || x.AttributeClass?.Name == "DllImportAttribute" || x.AttributeClass?.Name == "LibraryImportAttribute");
}
