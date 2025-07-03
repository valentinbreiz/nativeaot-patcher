using System.Collections.Concurrent;
using System.Collections.Immutable;
using Cosmos.Patcher.Analyzer.Extensions;
using Cosmos.Patcher.Analyzer.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Cosmos.Patcher.Analyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class PatcherAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NAOT";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        DiagnosticMessages.SupportedDiagnostics;

    private static readonly HashSet<string> s_externalTypeCache = [];

    public override void Initialize(AnalysisContext context)
    {
        DebugLog("[DEBUG] Initializing PatcherAnalyzer");
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            DebugLog($"[DEBUG] Starting compilation analysis for {compilationContext.Compilation.AssemblyName}");

            ConcurrentDictionary<string, PlugInfo> pluggedClasses = [];

            compilationContext.RegisterSyntaxNodeAction(
                nodeContext => AnalyzePlugAttribute(nodeContext, pluggedClasses),
                SyntaxKind.Attribute);
            compilationContext.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeAccessedMember(nodeContext, pluggedClasses),
                SyntaxKind.SimpleMemberAccessExpression);
            compilationContext.RegisterSyntaxTreeAction(treeContext => AnalyzeSyntaxTree(treeContext, pluggedClasses));

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
            if (plugInfo.IsExternal && s_externalTypeCache.Contains(classSymbol.Name))
            {
                DebugLog($"[DEBUG] Skipping validated external type {classSymbol.Name}");
                return;
            }

            if (!CheckIfNeedsPlug(accessedMethod, plugInfo.PlugSymbol))
                return;

            DebugLog($"[DEBUG] Reporting MethodNeedsPlug for {accessedMethod.Name}");
            ImmutableDictionary<string, string> properties = ImmutableDictionary.CreateRange([
                new KeyValuePair<string, string>("PlugClass", plugInfo.PlugSymbol.Name)
            ]);

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticMessages.MethodNeedsPlug,
                memberAccess.Expression.GetLocation(),
                properties!,
                accessedMethod.Name,
                classSymbol.Name
            ));
        }
        else
        {
            DebugLog($"[DEBUG] No plugged class found for {classSymbol.Name}");
            foreach (ISymbol member in classSymbol.GetMembers())
            {
                if (member is not IMethodSymbol { MethodKind: MethodKind.Ordinary } method ||
                    !CheckIfNeedsPlug(method, null))
                    continue;

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

    private void AnalyzePlugAttribute(SyntaxNodeAnalysisContext context,
        ConcurrentDictionary<string, PlugInfo> pluggedClasses)
    {
        DebugLog($"[DEBUG] AnalyzePlugAttribute started at {context.Node.GetLocation().GetMappedLineSpan()}");
        if (context.Node is not AttributeSyntax attribute || attribute.Name.ToString() != "Plug")
        {
            DebugLog("[DEBUG] Node is not Plug attribute");
            return;
        }

        ClassDeclarationSyntax? plugClass = attribute
            .Ancestors()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault();

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
        if (string.IsNullOrEmpty(pluggedClassName))
            return;

        string expectedName = $"{pluggedClassName}Impl";
        DebugLog($"[DEBUG] Expected plug class name: {expectedName}");
        if (classDeclarationSyntax.Identifier.Text == expectedName)
            return;

        DebugLog($"[DEBUG] Reporting PlugNameMismatch for {classDeclarationSyntax.Identifier.Text}");
        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticMessages.PlugNameDoesNotMatch,
            classDeclarationSyntax.GetLocation(),
            ImmutableDictionary.CreateRange([new KeyValuePair<string, string?>("ExpectedName", expectedName)]),
            classDeclarationSyntax.Identifier.Text,
            expectedName));
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
        INamedTypeSymbol? plugSymbol = context.SemanticModel.GetDeclaredSymbol(plugClass);
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

        List<ISymbol> symbolMembers = [.. symbol.GetMembers()];
        PlugInfo entry = pluggedClasses[symbol.Name];
        bool memberNeedsPlug = false;

        if (!(entry.IsExternal || s_externalTypeCache.Contains(symbol.Name)))
        {
            DebugLog($"[DEBUG] Checking {symbolMembers.Count} methods");
            foreach (ISymbol member in symbolMembers)
            {
                DebugLog($"[DEBUG] Checking method {member.Name}");
                if (member is IMethodSymbol { MethodKind: not MethodKind.Ordinary })
                {
                    DebugLog($"[DEBUG] Skipping non-ordinary method {member.Name}");
                    continue;
                }

                if (!CheckIfNeedsPlug(member, plugSymbol))
                    continue;

                memberNeedsPlug = true;
                DebugLog($"[DEBUG] Reporting MethodNeedsPlug for {member.Name}");
                ImmutableDictionary<string, string> diagnosticProperties = ImmutableDictionary.CreateRange(new[]
                {
                    new KeyValuePair<string, string?>("ClassName", plugClass.Identifier.Text),
                    new KeyValuePair<string, string?>("MethodName", member.Name)
                })!;

                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticMessages.MethodNeedsPlug,
                    plugClass.GetLocation(),
                    diagnosticProperties,
                    member.Name,
                    symbol.Name));
            }

            if (entry.IsExternal && !memberNeedsPlug)
                s_externalTypeCache.Add(symbol.Name);
        }

        List<IMethodSymbol> methods = [.. symbolMembers.OfType<IMethodSymbol>()];

        AnalyzePluggedClassCtors(plugClass, symbol, methods, context);
        foreach (MemberDeclarationSyntax member in plugClass.Members)
        {
            string? name = member.GetName();
            if (name is "Ctor" or "CCtor" ||
                symbolMembers.Any(x => x is IMethodSymbol method
                    ? method.MethodKind == MethodKind.Ordinary && method.Name == name
                    : x.Name == name))
                continue;

            DebugLog($"[DEBUG] Reporting MethodNotImplemented for {name}");
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticMessages.MethodNotImplemented,
                plugClass.GetLocation(),
                name,
                symbol.Name));
        }
    }

    private void AnalyzePluggedClassCtors(ClassDeclarationSyntax plugClass, INamedTypeSymbol symbol,
        List<IMethodSymbol> methods, SyntaxNodeAnalysisContext context)
    {
        DebugLog($"[DEBUG] AnalyzePluggedClassCtors for {symbol.Name}");
        if (plugClass.TryGetMemberByName("CCtor", out MethodDeclarationSyntax? cctor))
        {
            DebugLog("[DEBUG] Found CCtor method");
            if (methods.All(x => x.MethodKind != MethodKind.StaticConstructor))
            {
                DebugLog("[DEBUG] Reporting missing static constructor");
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticMessages.MethodNotImplemented,
                    plugClass.GetLocation(),
                    "CCtor(.cctor)",
                    symbol.Name));
                return;
            }

            DebugLog($"[DEBUG] CCtor has {cctor?.ParameterList.Parameters.Count} parameters");
            if (cctor?.ParameterList.Parameters.Count <= 1 || methods.Any(param => param.Name == "aThis"))
                return;

            DebugLog("[DEBUG] Reporting static constructor too many params");
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticMessages.StaticConstructorTooManyParams,
                plugClass.GetLocation(),
                cctor?.Identifier.Text));
        }
        else if (plugClass.TryGetMemberByName("Ctor", out ClassDeclarationSyntax? ctor) &&
                 methods.All(x => x.Name != ".ctor"))
        {
            DebugLog("[DEBUG] Reporting missing instance constructor");
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticMessages.MethodNotImplemented,
                ctor?.Identifier.GetLocation(),
                ".ctor",
                symbol.Name));
        }
    }

    private static bool CheckIfNeedsPlug(ISymbol symbol, INamedTypeSymbol? plugSymbol)
    {
        DebugLog($"[DEBUG] CheckIfNeedsPlug for {symbol.Name}");

        bool hasMethod = plugSymbol?.GetMembers(symbol.Name)
                             .Any(m => m.HasAttribute("PlugMemberAttribute") &&
                                       m.Name == symbol.Name) ??
                         false;

        bool hasSpecialAttributes =
            symbol.HasAttribute("MethodImplAttribute", "DllImportAttribute", "LibraryImportAttribute");
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
