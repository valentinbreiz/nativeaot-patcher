using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Liquip.Patcher.Analyzer;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;


namespace Liquip.Patcher.Analzyer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class PatcherAnalyzer : DiagnosticAnalyzer
{
    public const string AnalzyerDiagnosticId = "NAOT";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => DiagnosticMessages.SupportedDiagnostics;

    public Dictionary<string, ClassDeclarationSyntax> PluggedClasses = [];


    public override void Initialize(AnalysisContext context)
    {

        Console.WriteLine("[PatcherAnalyzer] Initializing...");
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzePlugAttribute, SyntaxKind.Attribute);
        context.RegisterSyntaxNodeAction(AnalyzeAccessedMember, SyntaxKind.SimpleMemberAccessExpression);
    }

    private void AnalyzeAccessedMember(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not MemberAccessExpressionSyntax elementAccessExpressionSyntax)
            return;

        Console.WriteLine($"[AnalyzeAccessedMember] Identifier is: {elementAccessExpressionSyntax.Expression}");
        ISymbol? symbol = context.SemanticModel.GetSymbolInfo(elementAccessExpressionSyntax.Expression).Symbol;

        if (symbol == null || symbol is not INamespaceOrTypeSymbol classSymbol)
            return;

        if (context.SemanticModel.GetSymbolInfo(elementAccessExpressionSyntax).Symbol is not IMethodSymbol accessedMethod)
            return;


        ImmutableDictionary<string, string> defaultProperties = ImmutableDictionary.CreateRange([new KeyValuePair<string, string?>("MethodName", accessedMethod.Name),
                    new KeyValuePair<string, string?>("ClassName", classSymbol.Name)]);

        if (PluggedClasses.TryGetValue(classSymbol.Name, out ClassDeclarationSyntax plugClass))
        {
            Console.WriteLine($"[AnalyzeAccessedMember] Found plugged class: {plugClass.Identifier.Text}.");
            bool methodExists = plugClass.Members
                .OfType<MethodDeclarationSyntax>()
                .Any(method => method.Identifier.Text == accessedMethod.Name);

            if (!methodExists && CheckIfNeedsPlug(accessedMethod, context, plugClass))
            {
                Console.WriteLine($"[AnalyzeAccessedMember] Method {accessedMethod.Name} does not exist in the plugged class {plugClass.Identifier.Text}.");
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticMessages.MethodNeedsPlug,
                    elementAccessExpressionSyntax.Expression.GetLocation(),
                    accessedMethod.Name,
                    symbol.Name,
                   ImmutableDictionary.CreateRange([
                    .. defaultProperties,
                    new KeyValuePair<string, string?>("PluggedClass", plugClass.Identifier.Text)
                    ])));
            }
        }
        else
        {
            IEnumerable<IMethodSymbol> methods = classSymbol.GetMembers()
               .OfType<IMethodSymbol>()
               .Where(method => method.MethodKind == MethodKind.Ordinary && CheckIfNeedsPlug(method, context, plugClass) == true);

            foreach (IMethodSymbol method in methods)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                                DiagnosticMessages.MethodNeedsPlug,
                                elementAccessExpressionSyntax.Expression.GetLocation(), method.Name, symbol.Name, defaultProperties));
            }
        }
    }

    private void AnalyzePlugAttribute(SyntaxNodeAnalysisContext context)
    {

        if (context.Node is not AttributeSyntax attribute || attribute.Name.ToString() != "Plug")
        {
            Console.WriteLine($"[AnalyzePlugAttribute] Not a Plug attribute. Node: {context.Node}");
            return;
        }

        ClassDeclarationSyntax plugClass = attribute.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        if (plugClass == null)
            return;

        Console.WriteLine($"[AnalyzePlugAttribute] Found Plug attribute. Attribute: {attribute}");

        // Get the target name from the attribute
        string targetName = GetAttributeValue<string>(attribute, 0, context) // Try to get the first argument
                            ?? GetAttributeValue<string>(attribute, "TargetName", context) // Try to get the named argument "TargetName"
                            ?? GetAttributeValue<Type>(attribute, "Target", context)?.FullName // Try to get the named argument "Target" as a Type
                            ?? GetAttributeValue<Type>(attribute, 0, context)?.FullName; // Try to get the first argument


        if (string.IsNullOrWhiteSpace(targetName))
        {
            Console.WriteLine("[AnalyzePlugAttribute] Target name is null or empty.");
            return;
        }

        Console.WriteLine($"[AnalyzePlugAttribute] Target Name: {targetName}");

        string assemblyName = context.Compilation.AssemblyName ?? string.Empty;
        string typeName = targetName ?? string.Empty;

        if (targetName.Contains(','))
        {
            string[] statement = targetName?.Split(',') ?? [];
            assemblyName = statement.Last().Trim();
            typeName = statement.First().Trim();
        }



        Console.WriteLine($"[AnalyzePlugAttribute] Assembly Name: {assemblyName}, Type Name: {typeName}");

        INamedTypeSymbol? symbol = context.Compilation.GetTypeByMetadataName(typeName ?? string.Empty);
        bool existInAssembly = symbol != null ||
                               context.Compilation.ExternalReferences.Any(x => x.Display != null && x.Display == assemblyName);

        Console.WriteLine($"[AnalyzePlugAttribute] Exist in Assembly: {existInAssembly}");

        if (!existInAssembly && !GetAttributeValue<bool>(attribute, "IsOptional", context))
        {
            var diagnostic = Diagnostic.Create(DiagnosticMessages.TypeNotFound, attribute.GetLocation(), targetName);
            Console.WriteLine($"[AnalyzePlugAttribute] Reporting diagnostic: {diagnostic}");
            context.ReportDiagnostic(diagnostic);
        }
        else
        {
            Console.WriteLine("[AnalyzePlugAttribute] Analyzing plugged class...");
            AnalyzePlugClass(plugClass, symbol?.Name, context);
            AnalyzePluggedClass(symbol, plugClass, context);
        }
    }

    private void AnalyzePlugClass(ClassDeclarationSyntax classDeclarationSyntax, string pluggedClassName, SyntaxNodeAnalysisContext context)
    {
        Console.WriteLine("[AnalyzePlugClass] Analyzing...");

        if (!classDeclarationSyntax.Modifiers.Any(SyntaxKind.StaticKeyword))
        {
            Console.WriteLine($"[AnalyzePlugClass] Class {classDeclarationSyntax.Identifier.Text} is not static but has Plug attribute.");
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticMessages.PlugNotStatic, classDeclarationSyntax.GetLocation(), classDeclarationSyntax.Identifier.Text));
        }

        if (string.IsNullOrEmpty(pluggedClassName)) return;
        string expectedName = $"{pluggedClassName}Impl";
        if (classDeclarationSyntax.Identifier.Text != expectedName)
        {
            Console.WriteLine($"[AnalyzePlugClass] Class {classDeclarationSyntax.Identifier.Text} does not match {expectedName}");
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticMessages.PlugNameDoesNotMatch, classDeclarationSyntax.GetLocation(), classDeclarationSyntax.Identifier.Text, expectedName, ImmutableDictionary.CreateRange([new KeyValuePair<string, string>("ExpectedName", expectedName)])));
        }
    }

    private void AnalyzePluggedClass(INamedTypeSymbol? symbol, ClassDeclarationSyntax? plugClass, SyntaxNodeAnalysisContext context)
    {
        if (plugClass == null || symbol == null)
        {
            return;
        }

        PluggedClasses[symbol.Name] = plugClass;

        IDictionary<string, MethodDeclarationSyntax> plugMethods = plugClass.Members
            .OfType<MethodDeclarationSyntax>()
            .ToDictionary(m => m.Identifier.Text);

        IEnumerable<IMethodSymbol> methodSymbols = symbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(x => x.MethodKind == MethodKind.Ordinary);


        foreach (IMethodSymbol? method in methodSymbols)
        {

            if (CheckIfNeedsPlug(method, context, plugClass))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        DiagnosticMessages.MethodNeedsPlug,
                        plugClass.GetLocation(),
ImmutableDictionary.CreateRange(new[]
                {
                new KeyValuePair<string, string?>("MethodName", method.Name)
            }), method.Name,
                        symbol.Name
                    )
                );
            }
        }

        foreach (MethodDeclarationSyntax? unimplemented in plugMethods.Values
            .Where(x => !methodSymbols.Any(m => m.Name == x.Identifier.Text)))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticMessages.MethodNotImplemented,
                    unimplemented.GetLocation(),
                    unimplemented.Identifier.Text,
                   symbol.Name
                )
            );
        }
    }


    private static T GetAttributeValue<T>(object attribute, object indexOrString, SyntaxNodeAnalysisContext context)
    {
        Console.WriteLine($"[GetAttributeValue] Getting attribute value. Attribute: {attribute}, IndexOrString: {indexOrString}");

        if (attribute is AttributeData attributeData)
        {
            if (attributeData.ConstructorArguments.Length == 0)
                return default!;

            TypedConstant argument = indexOrString switch
            {
                int nameInt when nameInt >= 0 && nameInt < attributeData.ConstructorArguments.Length => attributeData.ConstructorArguments[nameInt],
                string nameString => attributeData.NamedArguments.FirstOrDefault(x => StringComparer.OrdinalIgnoreCase.Equals(x.Key, nameString)).Value,
                _ => default,
            };


            if (typeof(T).IsEnum && argument.Value != null)
            {
                return (T)Enum.Parse(typeof(T), argument.Value.ToString());
            }
            else if (argument.Value is T value)
            {
                return value;
            }
            else if (typeof(T) == typeof(Type) && argument.Value != null)
            {
                string typeFullName = $"{argument.Type?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? string.Empty}, {argument.Type?.ContainingAssembly.Name ?? string.Empty}";
                return (T)(object)Type.GetType(typeFullName, true);
            }
        }
        else if (attribute is AttributeSyntax attributeSyntax)
        {
            ExpressionSyntax? argument = indexOrString switch
            {
                int nameInt when nameInt >= 0 && nameInt < attributeSyntax.ArgumentList?.Arguments.Count => attributeSyntax.ArgumentList.Arguments[nameInt].Expression,
                string nameString => attributeSyntax.ArgumentList.Arguments.FirstOrDefault(x => x.NameEquals != null && StringComparer.OrdinalIgnoreCase.Equals(x.NameEquals.Name.ToString(), nameString))?.Expression,
                _ => default,
            };


            if (argument is MemberAccessExpressionSyntax memberAccess && typeof(T).IsEnum)
            {
                return (T)Enum.Parse(typeof(T), memberAccess.Name.ToString());
            }
            else if (argument is LiteralExpressionSyntax literal && literal.Token.Value is T value)
            {
                return value;
            }

            else if (argument is TypeOfExpressionSyntax typeOf)
            {
                string typeName = $"{typeOf.Type.FirstAncestorOrSelf<NamespaceDeclarationSyntax>()?.Name}.{typeOf.Type}";
                ISymbol? symbol = context.Compilation.GetTypeByMetadataName(typeName);

                if (symbol != null)
                {

                    string typeFullName = $"{symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}, {symbol.ContainingAssembly.Name}";
                    var typeVal = Type.GetType(typeFullName, true);
                    return (T)(object)typeVal;

                }
            }
        }

        return default!;
    }

    private static bool CheckIfNeedsPlug(IMethodSymbol methodSymbol, SyntaxNodeAnalysisContext context, ClassDeclarationSyntax plugClass = default)
    {
        if (plugClass != null && plugClass.TryGetMemberByName(methodSymbol.Name, out MethodDeclarationSyntax _)) return false;

        ImmutableArray<AttributeData> attributes = methodSymbol.GetAttributes();
        bool isInternalCall = attributes.Any(x =>
        {

            return x.AttributeClass?.Name == "MethodImplAttribute" && GetAttributeValue<MethodImplOptions>(x, 0, context).HasFlag(MethodImplOptions.InternalCall);

        });

        bool isDllImport = attributes.Any(x =>
        {
            Console.WriteLine(x.AttributeClass?.Name);
            return x.AttributeClass?.Name == "DllImportAttribute" || x.AttributeClass?.Name == "LibraryImportAttribute";
        });
        return isInternalCall || isDllImport;
    }
}
