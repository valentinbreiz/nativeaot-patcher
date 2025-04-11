using System.Collections.Concurrent;
using System.Collections.Immutable;
using Cosmos.Patcher.Analyzer.Extensions;
using Cosmos.Patcher.Analyzer.Models;
using Cosmos.Patcher.Analyzer.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Cosmos.Patcher.Analyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class PatcherAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NAOT";
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => DiagnosticMessages.SupportedDiagnostics;

    private static readonly HashSet<string> _validatedExternals = [];

    public override void Initialize(AnalysisContext context)
    {
        DebugLog("[DEBUG] Initializing PatcherAnalyzer");
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction((CompilationStartAnalysisContext compilationContext) =>
        {
            DebugLog($"[DEBUG] Starting compilation analysis for {compilationContext.Compilation.AssemblyName}");

            ConcurrentDictionary<string, PlugInfo> pluggedClasses = [];

            compilationContext.RegisterSyntaxNodeAction(context => AnalyzePlugAttribute(context, pluggedClasses),
                SyntaxKind.Attribute);
            compilationContext.RegisterSyntaxNodeAction(context => AnalyzeAccessedMember(context, pluggedClasses),
                SyntaxKind.SimpleMemberAccessExpression);
            compilationContext.RegisterSyntaxTreeAction(context => AnalyzeSyntaxTree(context, pluggedClasses));

            DebugLog(
                $"[DEBUG] Registered syntax node actions for compilation {compilationContext.Compilation.AssemblyName}");
        });
    }

    private void AnalyzeSyntaxTree(SyntaxTreeAnalysisContext context,
        ConcurrentDictionary<string, PlugInfo> pluggedClasses)
    {
        DebugLog($"[DEBUG] AnalyzeSyntaxTree started for {context.Tree.FilePath}");
        SyntaxNode syntaxRoot = context.Tree.GetRoot(context.CancellationToken);
        foreach (KeyValuePair<string, PlugInfo> kvp in pluggedClasses)
        {
            if (!kvp.Value.PlugSymbol.DeclaringSyntaxReferences.Any(r => r.SyntaxTree == context.Tree))
            {
                pluggedClasses.TryRemove(kvp.Key, out _);
                DebugLog($"[DEBUG] Removed {kvp.Key} from pluggedClasses due to missing syntax reference");
            }
        }
    }

    private void AnalyzeAccessedMember(
        SyntaxNodeAnalysisContext context,
        ConcurrentDictionary<string, PlugInfo> pluggedClasses)
    {
        DebugLog($"[DEBUG] AnalyzeAccessedMember started at {context.Node.GetLocation().GetMappedLineSpan()}");

        if (context.Node is not MemberAccessExpressionSyntax memberAccess)
        {
            DebugLog("[DEBUG] Node is not MemberAccessExpressionSyntax");
            return;
        }

        ISymbol? symbol = context.SemanticModel.GetSymbolInfo(memberAccess.Expression).Symbol;
        if (symbol is not INamedTypeSymbol classSymbol)
        {
            DebugLog("[DEBUG] Symbol is not a named type symbol");
            return;
        }

        if (context.SemanticModel.GetSymbolInfo(memberAccess).Symbol is not IMethodSymbol accessedMethod)
        {
            DebugLog("[DEBUG] Accessed symbol is not a method");
            return;
        }

        DebugLog($"[DEBUG] Checking accessed method {accessedMethod.Name} in class {classSymbol.Name}");

        if (pluggedClasses.TryGetValue(classSymbol.Name, out PlugInfo plugInfo))
        {
            DebugLog($"[DEBUG] Found plugged class {classSymbol.Name}");

            // Skip validation for external types that have already been checked
            if (plugInfo.IsExternal && _validatedExternals.Contains(classSymbol.Name))
            {
                DebugLog($"[DEBUG] Skipping validated external type {classSymbol.Name}");
                return;
            }

            if (CheckIfNeedsPlug(accessedMethod, plugInfo.PlugSymbol))
            {
                DebugLog($"[DEBUG] Reporting MethodNeedsPlug for {accessedMethod.Name}");
                ImmutableDictionary<string, string> properties = ImmutableDictionary.CreateRange(new[]
                {
                    new KeyValuePair<string, string?>("PlugClass", plugInfo.PlugSymbol.Name)
                });

                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticMessages.MethodNeedsPlug,
                    memberAccess.Expression.GetLocation(),
                    properties,
                    accessedMethod.Name,
                    classSymbol.Name
                ));
            }
        }
        else
        {
            DebugLog($"[DEBUG] No plugged class found for {classSymbol.Name}");
            foreach (ISymbol member in classSymbol.GetMembers())
            {
                if (member is IMethodSymbol method &&
                    method.MethodKind == MethodKind.Ordinary &&
                    CheckIfNeedsPlug(method, null))
                {
                    DebugLog($"[DEBUG] Reporting MethodNeedsPlug for {method.Name}");
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticMessages.MethodNeedsPlug,
                        memberAccess.Expression.GetLocation(),
                        properties: null,
                        method.Name,
                        classSymbol.Name
                    ));
                }
            }
        }
    }

    private void AnalyzePlugAttribute(SyntaxNodeAnalysisContext context,
        ConcurrentDictionary<string, PlugInfo> pluggedClasses)
    {
        DebugLog($"[DEBUG] AnalyzePlugAttribute started at {context.Node.GetLocation().GetMappedLineSpan()}");
        if (context.Node is not AttributeSyntax attribute || attribute.Name.ToString() != "Plug")
        {
            DebugLog("[DEBUG] Node is not Plug attribute");
            return;
        }

        ClassDeclarationSyntax? plugClass = attribute.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        if (plugClass == null)
        {
            DebugLog("[DEBUG] No containing class found for Plug attribute");
            return;
        }

        DebugLog($"[DEBUG] Found Plug attribute on class {plugClass.Identifier.Text}");
        bool hasTargetName = attribute.GetAttributeValue("TargetName", context, out string? targetName) ||
                             attribute.GetAttributeValue(0, context, out targetName);
        if (!hasTargetName)
        {
            DebugLog("[DEBUG] Trying to get Target attribute value");
            if (!(attribute.GetAttributeValue("Target", context, out string? type) ||
                  attribute.GetAttributeValue(0, context, out type)))
            {
                DebugLog("[DEBUG] Couldn't get Target value");
                return;
            }

            targetName = type;
        }

        DebugLog($"[DEBUG] Resolved targetName: {targetName}");
        if (string.IsNullOrEmpty(targetName))
        {
            DebugLog("[DEBUG] targetName is null or empty");
            return;
        }

        string assemblyName = context.Compilation.AssemblyName ?? string.Empty;
        string typeName = targetName;

        if (targetName.Contains(','))
        {
            DebugLog("[DEBUG] Splitting targetName with comma");
            string[] statement = targetName.Split(',');
            typeName = statement[0].Trim();
            assemblyName = statement.Last().Trim();
        }

        DebugLog($"[DEBUG] Looking for type {typeName} in assembly {assemblyName}");
        INamedTypeSymbol? symbol = context.Compilation.GetTypeByMetadataName(typeName);
        bool existInAssembly = symbol != null ||
                               context.Compilation.ExternalReferences.Any(x =>
                                   x.Display != null && x.Display == assemblyName);

        DebugLog($"[DEBUG] Type exists in assembly: {existInAssembly}");
        if (!existInAssembly && !attribute.GetAttributeValue<bool>("IsOptional", context))
        {
            DebugLog($"[DEBUG] Reporting TypeNotFound for {targetName}");
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticMessages.TypeNotFound, attribute.GetLocation(),
                targetName));
            return;
        }

        if (symbol == null)
        {
            DebugLog("[DEBUG] Symbol is null");
            return;
        }

        DebugLog($"[DEBUG] Analyzing plug class {plugClass.Identifier.Text} for {symbol.Name}");
        AnalyzePlugClass(plugClass, symbol.Name, context);
        AnalyzePluggedClass(symbol, plugClass, context, pluggedClasses);
    }

    private void AnalyzePlugClass(ClassDeclarationSyntax classDeclarationSyntax, string pluggedClassName,
        SyntaxNodeAnalysisContext context)
    {
        DebugLog($"[DEBUG] AnalyzePlugClass for {classDeclarationSyntax.Identifier.Text}");
        if (!classDeclarationSyntax.Modifiers.Any(SyntaxKind.StaticKeyword))
        {
            DebugLog($"[DEBUG] Reporting PlugNotStatic for {classDeclarationSyntax.Identifier.Text}");
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticMessages.PlugNotStatic,
                classDeclarationSyntax.GetLocation(),
                classDeclarationSyntax.Identifier.Text));
        }

        if (!string.IsNullOrEmpty(pluggedClassName))
        {
            string expectedName = $"{pluggedClassName}Impl";
            DebugLog($"[DEBUG] Expected plug class name: {expectedName}");
            if (classDeclarationSyntax.Identifier.Text != expectedName)
            {
                DebugLog($"[DEBUG] Reporting PlugNameMismatch for {classDeclarationSyntax.Identifier.Text}");
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticMessages.PlugNameDoesNotMatch,
                    classDeclarationSyntax.GetLocation(),
                    ImmutableDictionary.CreateRange(new[]
                    {
                        new KeyValuePair<string, string?>("ExpectedName", expectedName)
                    }),
                    classDeclarationSyntax.Identifier.Text,
                    expectedName));
            }
        }
    }

    private void AnalyzePluggedClass(INamedTypeSymbol? symbol, ClassDeclarationSyntax? plugClass,
        SyntaxNodeAnalysisContext context, ConcurrentDictionary<string, PlugInfo> pluggedClasses)
    {
        DebugLog($"[DEBUG] AnalyzePluggedClass for {symbol?.Name ?? "null"}");
        if (plugClass == null || symbol == null)
        {
            DebugLog("[DEBUG] plugClass or symbol is null");
            return;
        }

        bool isExternalSymbol =
            !SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, context.Compilation.Assembly);
        INamedTypeSymbol plugSymbol = context.SemanticModel.GetDeclaredSymbol(plugClass);
        DebugLog($"[DEBUG] Is external symbol: {isExternalSymbol}");
        pluggedClasses.TryAdd(symbol.Name, new PlugInfo(isExternalSymbol, plugSymbol));
        DebugLog($"[DEBUG] Added {symbol.Name} to pluggedClasses");
        AnalyzePluggedClassMembers(plugClass, plugSymbol, symbol, context, pluggedClasses);
    }

    private void AnalyzePluggedClassMembers(ClassDeclarationSyntax plugClass, INamedTypeSymbol plugSymbol,
        INamedTypeSymbol symbol, SyntaxNodeAnalysisContext context,
        ConcurrentDictionary<string, PlugInfo> pluggedClasses)
    {
        DebugLog($"[DEBUG] AnalyzePluggedClassMembers for {symbol.Name}");
        IEnumerable<IMethodSymbol> methods = symbol.GetMembers().OfType<IMethodSymbol>();
        PlugInfo entry = pluggedClasses[symbol.Name];
        bool anyMethodsNeedPlug = false;

        if (!(entry.IsExternal || _validatedExternals.Contains(symbol.Name)))
        {
            DebugLog($"[DEBUG] Checking {methods.Count()} methods");
            foreach (IMethodSymbol method in methods)
            {
                DebugLog($"[DEBUG] Checking method {method.Name}");
                if (method.MethodKind is not MethodKind.Ordinary)
                {
                    DebugLog($"[DEBUG] Skipping non-ordinary method {method.Name}");
                    continue;
                }

                if (CheckIfNeedsPlug(method, plugSymbol))
                {
                    anyMethodsNeedPlug = true;
                    DebugLog($"[DEBUG] Reporting MethodNeedsPlug for {method.Name}");
                    ImmutableDictionary<string, string> diagnosticProperties = ImmutableDictionary.CreateRange(new[]
                    {
                        new KeyValuePair<string, string?>("ClassName", plugClass.Identifier.Text),
                        new KeyValuePair<string, string?>("MethodName", method.Name)
                    })!;

                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticMessages.MethodNeedsPlug,
                        plugClass.GetLocation(),
                        diagnosticProperties,
                        method.Name,
                        symbol.Name));
                }
            }

            if (entry.IsExternal && !anyMethodsNeedPlug)
            {
                _validatedExternals.Add(symbol.Name);
            }
        }

        AnalyzePluggedClassCtors(plugClass, symbol, methods, context);
        foreach (MemberDeclarationSyntax member in plugClass.Members)
        {
            if (member is MethodDeclarationSyntax unimplemented &&
                unimplemented.Identifier.Text is not ("Ctor" or "CCtor") &&
                !methods.Any(x => x.MethodKind == MethodKind.Ordinary && x.Name == unimplemented.Identifier.Text))
            {
                DebugLog($"[DEBUG] Reporting MethodNotImplemented for {unimplemented.Identifier.Text}");
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticMessages.MethodNotImplemented,
                    plugClass.GetLocation(),
                    unimplemented.Identifier.Text,
                    symbol.Name));
            }
        }
    }

    private void AnalyzePluggedClassCtors(ClassDeclarationSyntax plugClass, INamedTypeSymbol symbol,
        IEnumerable<IMethodSymbol> methods, SyntaxNodeAnalysisContext context)
    {
        DebugLog($"[DEBUG] AnalyzePluggedClassCtors for {symbol.Name}");
        if (plugClass.TryGetMemberByName("CCtor", out MethodDeclarationSyntax? cctor))
        {
            DebugLog("[DEBUG] Found CCtor method");
            if (!methods.Any(x => x.MethodKind == MethodKind.StaticConstructor))
            {
                DebugLog("[DEBUG] Reporting missing static constructor");
                DebugLog($"[DEBUG] Location:{cctor.GetFullMethodLocation()}, Span:{cctor.Span}");
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticMessages.MethodNotImplemented,
                    plugClass.GetLocation(),
                    "CCtor(.cctor)",
                    symbol.Name));
                return;
            }

            DebugLog($"[DEBUG] CCtor has {cctor.ParameterList.Parameters.Count} parameters");
            if (cctor.ParameterList.Parameters.Count > 1 && !methods.Any(param => param.Name == "aThis"))
            {
                DebugLog("[DEBUG] Reporting static constructor too many params");
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticMessages.StaticConstructorTooManyParams,
                    plugClass.GetLocation(),
                    cctor.Identifier.Text));
            }
        }
        else if (plugClass.TryGetMemberByName("Ctor", out ClassDeclarationSyntax? ctor) &&
                 !methods.Any(x => x.Name == ".ctor"))
        {
            DebugLog("[DEBUG] Reporting missing instance constructor");
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticMessages.MethodNotImplemented,
                ctor.Identifier.GetLocation(),
                ".ctor",
                symbol.Name));
        }
    }

    private static bool CheckIfNeedsPlug(IMethodSymbol methodSymbol, INamedTypeSymbol? plugSymbol)
    {
        DebugLog($"[DEBUG] CheckIfNeedsPlug for {methodSymbol.Name}");

        bool hasMethod = plugSymbol?.GetMembers(methodSymbol.Name)
            .OfType<IMethodSymbol>()
            .Any(m => SymbolEqualityComparer.Default.Equals(m, methodSymbol)) ?? false;

        bool hasSpecialAttributes = methodSymbol.GetAttributes().Any(attr =>
            attr.AttributeClass?.Name is "MethodImplAttribute" or "DllImportAttribute" or "LibraryImportAttribute");

        DebugLog($"[DEBUG] hasMethodInPlugClass: {hasMethod}, hasSpecialAttributes: {hasSpecialAttributes}");
        return !hasMethod && hasSpecialAttributes;
    }

    private static void DebugLog(string message)
    {
#if DEBUG
        Console.WriteLine(message);
#endif
    }
}
