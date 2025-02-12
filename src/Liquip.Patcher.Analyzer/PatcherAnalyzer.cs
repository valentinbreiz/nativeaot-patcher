using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Liquip.Patcher.Analyzer.Extensions;
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

    private readonly ConcurrentDictionary<string, ClassDeclarationSyntax> _pluggedClasses = new();

    public override void Initialize(AnalysisContext context)
    {
#if DEBUG
        DebugLogger.Log("Initializing...");
#endif
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
            {
                _pluggedClasses.TryRemove(pair.Key, out _);
            }
        }
    }

    private void AnalyzeAccessedMember(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not MemberAccessExpressionSyntax elementAccessExpressionSyntax)
            return;

#if DEBUG
        DebugLogger.Log($"Identifier is: {elementAccessExpressionSyntax.Expression}");
#endif

        ISymbol? symbol = context.SemanticModel.GetSymbolInfo(elementAccessExpressionSyntax.Expression).Symbol;
        if (symbol is not INamespaceOrTypeSymbol classSymbol || context.SemanticModel.GetSymbolInfo(elementAccessExpressionSyntax).Symbol is not IMethodSymbol accessedMethod)
            return;

        ImmutableDictionary<string, string?> defaultProperties = ImmutableDictionary.CreateRange(new[]
        {
            new KeyValuePair<string, string?>("MethodName", accessedMethod.Name),
            new KeyValuePair<string, string?>("ClassName", classSymbol.Name)
        });

        if (_pluggedClasses.TryGetValue(classSymbol.Name, out ClassDeclarationSyntax plugClass))
        {
#if DEBUG
            DebugLogger.Log($"Found plugged class: {plugClass.Identifier.Text}.");
#endif
            bool methodExists = plugClass.Members
                .OfType<MethodDeclarationSyntax>()
                .Any(method => method.Identifier.Text == accessedMethod.Name);

            if (!methodExists && CheckIfNeedsPlug(accessedMethod, context, plugClass))
            {
#if DEBUG
                DebugLogger.Log($"Method {accessedMethod.Name} does not exist in the plugged class {plugClass.Identifier.Text}.");
#endif
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
            foreach (IMethodSymbol? method in classSymbol.GetMembers().OfType<IMethodSymbol>()
                .Where(method => method.MethodKind == MethodKind.Ordinary && CheckIfNeedsPlug(method, context, plugClass)))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticMessages.MethodNeedsPlug,
                    elementAccessExpressionSyntax.Expression.GetLocation(),
                    defaultProperties,
                    method.Name,
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

#if DEBUG
        DebugLogger.Log($"Found Plug attribute. Attribute: {attribute}");
#endif

        // Get the target name from the attribute
        string? targetName = GetAttributeValue<string>(attribute, 0, context) ??
                             GetAttributeValue<string>(attribute, "TargetName", context) ??
                             GetAttributeValue<Type>(attribute, "Target", context)?.FullName ??
                             GetAttributeValue<Type>(attribute, 0, context)?.FullName;

        if (string.IsNullOrWhiteSpace(targetName))
            return;

#if DEBUG
        DebugLogger.Log($"Target Name: {targetName}");

#endif
        string assemblyName = context.Compilation.AssemblyName ?? string.Empty;
        string typeName = targetName!;

#if DEBUG
        DebugLogger.Log($"Assembly Name: {assemblyName}");

#endif
        if (targetName.Contains(','))
        {
            string[] statement = targetName!.Split(',');
            assemblyName = statement.Last().Trim();
            typeName = statement[0].Trim();
        }

        INamedTypeSymbol? symbol = context.Compilation.GetTypeByMetadataName(typeName);
        bool existInAssembly = symbol != null || context.Compilation.ExternalReferences
            .Any(x => x.Display != null && x.Display == assemblyName);

        if (!existInAssembly && !GetAttributeValue<bool>(attribute, "IsOptional", context))
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
            if (CheckIfNeedsPlug(method, context, plugClass))
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
#if DEBUG
            DebugLogger.Log($"Static Constructor Analysis: Method='{cctor.Identifier}', " +
                               $"ParameterCount={cctor.ParameterList.Parameters.Count}, " +
                               $"HasAThis={cctor.ParameterList.Parameters.Any(param => param.Identifier.Text == "aThis")}, " +
                               $"Class='{plugClass.Identifier.Text}'");
#endif

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

    private static T? GetAttributeValue<T>(object attribute, object indexOrString, SyntaxNodeAnalysisContext context)
    {
        if (attribute == null || indexOrString == null)
            return default;

        if (attribute is AttributeData attributeData)
        {
            return GetAttributeDataValue<T>(attributeData, indexOrString);
        }

        if (attribute is AttributeSyntax attributeSyntax)
        {
            return GetAttributeSyntaxValue<T>(attributeSyntax, indexOrString, context);
        }

        return default;
    }

    private static T? GetAttributeDataValue<T>(AttributeData attributeData, object indexOrString)
    {
        if (attributeData.ConstructorArguments.Length == 0)
            return default;

        TypedConstant argument = GetConstructorArgument(attributeData, indexOrString);

        if (argument.Value is T value)
            return value;

        if (typeof(T).IsEnum && argument.Value != null)
            return (T)Enum.Parse(typeof(T), argument.Value.ToString());

        if (typeof(T) == typeof(Type) && argument.Value != null)
            return (T)(object)Type.GetType($"{argument.Type?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? string.Empty}, {argument.Type?.ContainingAssembly.Name ?? string.Empty}", true);

        return default;
    }

    private static T? GetAttributeSyntaxValue<T>(AttributeSyntax attributeSyntax, object indexOrString, SyntaxNodeAnalysisContext context)
    {
        ExpressionSyntax? argument = GetArgumentSyntax(attributeSyntax, indexOrString);

        if (argument == null)
            return default;

        if (argument is MemberAccessExpressionSyntax memberAccess && typeof(T).IsEnum)
            return (T)Enum.Parse(typeof(T), memberAccess.Name.ToString());

        if (argument is LiteralExpressionSyntax literal && literal.Token.Value is T literalValue)
            return literalValue;

        if (argument is TypeOfExpressionSyntax typeOf)
        {
            ISymbol? symbol = context.Compilation.GetTypeByMetadataName($"{typeOf.Type.FirstAncestorOrSelf<BaseNamespaceDeclarationSyntax>()?.Name}.{typeOf.Type}");
            if (symbol != null)
                return (T)(object)Type.GetType($"{symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}, {symbol.ContainingAssembly.Name}", true);
        }
        return default;
    }

    private static TypedConstant GetConstructorArgument(AttributeData attributeData, object indexOrString)
    {
        return indexOrString switch
        {
            int nameInt when nameInt >= 0 && nameInt < attributeData.ConstructorArguments.Length => attributeData.ConstructorArguments[nameInt],
            string nameString => attributeData.NamedArguments
                .FirstOrDefault(x => StringComparer.OrdinalIgnoreCase.Equals(x.Key, nameString)).Value,
            _ => default,
        };
    }

    private static ExpressionSyntax? GetArgumentSyntax(AttributeSyntax attributeSyntax, object indexOrString)
    {
        return indexOrString switch
        {
            int nameInt when nameInt >= 0 && nameInt < attributeSyntax.ArgumentList?.Arguments.Count
                => attributeSyntax.ArgumentList.Arguments[nameInt].Expression,
            string nameString => attributeSyntax.ArgumentList?.Arguments
                .FirstOrDefault(x => x.NameEquals != null &&
                    string.Equals(x.NameEquals.Name.ToString(), nameString, StringComparison.OrdinalIgnoreCase))
                ?.Expression,
            _ => default,
        };
    }

    private static bool CheckIfNeedsPlug(IMethodSymbol methodSymbol, SyntaxNodeAnalysisContext context, ClassDeclarationSyntax plugClass) => plugClass?.TryGetMemberByName(methodSymbol.Name, out MethodDeclarationSyntax _) != true && methodSymbol.GetAttributes().Any(x => (x.AttributeClass?.Name == "MethodImplAttribute" && GetAttributeValue<MethodImplOptions>(x, 0, context).HasFlag(MethodImplOptions.InternalCall)) || x.AttributeClass?.Name == "DllImportAttribute" || x.AttributeClass?.Name == "LibraryImportAttribute");
}
