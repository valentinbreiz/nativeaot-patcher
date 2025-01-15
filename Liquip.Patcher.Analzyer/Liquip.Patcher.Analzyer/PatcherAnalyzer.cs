using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using System.Linq.Expressions;

namespace Liquip.Patcher.Analzyer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class PatcherAnalyzer : DiagnosticAnalyzer
{
    public const string AnalzyerDiagnosticId = "NAOT";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => DiagnosticMessages.SupportedDiagnostics;

    public HashSet<ClassDeclarationSyntax> PluggedClasses = [];

    private static readonly ILogger _logger = new Logger<PatcherAnalyzer>(new LoggerFactory(new[] { new ConsoleLoggerProvider(new OptionsMonitor<ConsoleLoggerOptions>(new OptionsFactory<ConsoleLoggerOptions>(Enumerable.Empty<IConfigureOptions<ConsoleLoggerOptions>>(), Enumerable.Empty<IPostConfigureOptions<ConsoleLoggerOptions>>()), Enumerable.Empty<IOptionsChangeTokenSource<ConsoleLoggerOptions>>(), new OptionsCache<ConsoleLoggerOptions>())) }));

    public override void Initialize(AnalysisContext context)
    {
        _logger.LogInformation("[PatcherAnalyzer] Initializing...");
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();


        context.RegisterSyntaxNodeAction(AnalyzePlugAttribute, SyntaxKind.Attribute);
        context.RegisterSyntaxNodeAction(AnalyzePlugClass, SyntaxKind.ClassDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeAccessedMember, SyntaxKind.SimpleMemberAccessExpression);
    }

    private void AnalyzeAccessedMember(SyntaxNodeAnalysisContext context)
    {
        _logger.LogInformation("[AnalyzeAccessedMember] Analyzing...");
        _logger.LogInformation($"[AnalyzeAccessedMember] Node: {context.Node}");
        
        if (context.Node is not MemberAccessExpressionSyntax elementAccessExpressionSyntax)
            return;

        if (elementAccessExpressionSyntax.Expression is InvocationExpressionSyntax invocationExpressionSyntax)
        {
            _logger.LogInformation($"[AnalyzeAccessedMember] Invocation Expression: {invocationExpressionSyntax}");
            if (context.SemanticModel.GetSymbolInfo(invocationExpressionSyntax.Expression).Symbol is IMethodSymbol methodSymbol && CheckIfNeedsPlug(methodSymbol, context))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticMessages.MethodNeedsPlug,
                    invocationExpressionSyntax.GetLocation(),
                    methodSymbol.Name));
            }
        } else {

        ClassDeclarationSyntax containingClass = elementAccessExpressionSyntax.FirstAncestorOrSelf<ClassDeclarationSyntax>();
        if (containingClass != null)
        {
            bool methodsNeedPlugs = containingClass.Members
                .OfType<MethodDeclarationSyntax>()
                .Any(method => CheckIfNeedsPlug(method, context));

            if (methodsNeedPlugs && !PluggedClasses.Contains(containingClass))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticMessages.ClassNeedsPlug,
                    elementAccessExpressionSyntax.GetLocation(),containingClass.Identifier.Text));
            }
        }
        }
    }


    private void AnalyzePlugAttribute(SyntaxNodeAnalysisContext context)
    {
        _logger.LogInformation("[AnalyzePlugAttribute] Analyzing...");

        if (context.Node is not AttributeSyntax attribute || attribute.Name.ToString() != "Plug")
        {
            _logger.LogInformation($"[AnalyzePlugAttribute] Not a Plug attribute. Node: {context.Node}");
            return;
        }

        _logger.LogInformation($"[AnalyzePlugAttribute] Found Plug attribute. Attribute: {attribute}");

        // Get the target name from the attribute
        string targetName = GetAttributeValue<string>(attribute, 0, context) // Try to get the first argument
                            ?? GetAttributeValue<string>(attribute, "TargetName", context) // Try to get the named argument "TargetName"
                            ?? GetAttributeValue<Type>(attribute, "Target", context)?.FullName // Try to get the named argument "Target" as a Type
                            ?? GetAttributeValue<Type>(attribute, 0, context)?.FullName; // Try to get the first argument

        if (string.IsNullOrWhiteSpace(targetName))
        {
            _logger.LogInformation("[AnalyzePlugAttribute] Target name is null or empty.");
            return;
        }

        _logger.LogInformation($"[AnalyzePlugAttribute] Target Name: {targetName}");

        string assemblyName = context.Compilation.AssemblyName ?? string.Empty;
        string typeName = targetName ?? string.Empty;

        if (targetName.Contains(','))
        {
            string[] statement = targetName?.Split(',') ?? [];
            assemblyName = statement.Last().Trim();
            typeName = statement.First().Trim();
        }

        _logger.LogInformation($"[AnalyzePlugAttribute] Assembly Name: {assemblyName}, Type Name: {typeName}");

        INamedTypeSymbol? symbol = context.Compilation.GetTypeByMetadataName(typeName ?? string.Empty);
        bool existInAssembly = symbol != null ||
                               context.Compilation.ExternalReferences.Any(x => x.Display != null && x.Display == assemblyName);

        _logger.LogInformation($"[AnalyzePlugAttribute] Exist in Assembly: {existInAssembly}");

        if (!existInAssembly && !GetAttributeValue<bool>(attribute, "IsOptional", context))
        {
            var diagnostic = Diagnostic.Create(DiagnosticMessages.TypeNotFound, attribute.GetLocation(), targetName);
            _logger.LogInformation($"[AnalyzePlugAttribute] Reporting diagnostic: {diagnostic}");
            context.ReportDiagnostic(diagnostic);
        }
        else
        {
            _logger.LogInformation("[AnalyzePlugAttribute] Analyzing plugged class...");
            AnalyzePluggedClass(symbol, context.Node.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault(), context);
        }
    }

    private void AnalyzePlugClass(SyntaxNodeAnalysisContext context)
    {
        _logger.LogInformation("[AnalyzePlugClass] Analyzing...");

        if (context.Node is not ClassDeclarationSyntax classDeclarationSyntax || !classDeclarationSyntax.AttributeLists.Any(x => x.Attributes.Any(attr => attr.Name.ToString() == "Plug")))
        {
            _logger.LogInformation($"[AnalyzePlugClass] Not a class declaration. Node: {context.Node}");
            return;
        }

        if (!classDeclarationSyntax.Modifiers.Any(SyntaxKind.StaticKeyword))
        {
            _logger.LogInformation($"[AnalyzePlugClass] Class {classDeclarationSyntax.Identifier.Text} is not static but has Plug attribute.");
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticMessages.PlugNotStatic, classDeclarationSyntax.GetLocation(), classDeclarationSyntax.Identifier.Text));
        }

        if (!classDeclarationSyntax.Identifier.Text.EndsWith("Impl"))
        {
            _logger.LogInformation($"[AnalyzePlugClass] Class {classDeclarationSyntax.Identifier.Text} does not have Impl suffix.");
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticMessages.PlugNameNeedsImplSuffix, classDeclarationSyntax.GetLocation(), classDeclarationSyntax.Identifier.Text));
        }
    }

    private void AnalyzePluggedClass(INamedTypeSymbol? symbol, ClassDeclarationSyntax? plugClass, SyntaxNodeAnalysisContext context)
    {
        _logger.LogInformation("[AnalyzePluggedClass] Analyzing...");

        if (plugClass == null || symbol == null)
        {
            _logger.LogInformation("[AnalyzePluggedClass] Plug class or symbol is null.");
            return;
        }

        PluggedClasses.Add(plugClass);
        _logger.LogInformation($"[AnalyzePluggedClass] Plug class: {plugClass.Identifier.Text}, Symbol: {symbol.Name}");

        IEnumerable<string> plugMethods = plugClass.Members.OfType<MethodDeclarationSyntax>().Select(x => x.Identifier.Text);
        foreach (IMethodSymbol method in symbol.GetMembers().OfType<IMethodSymbol>().Where(x => x.MethodKind == MethodKind.Ordinary))
        {
            _logger.LogInformation($"[AnalyzePluggedClass] Analyzing method: {method.Name}");

            if (CheckIfNeedsPlug(method, context))
            {
                _logger.LogInformation($"[AnalyzePluggedClass] Method {method.Name} requires a plug.");
                context.ReportDiagnostic(Diagnostic.Create(DiagnosticMessages.MethodNeedsPlug, context.Node.GetLocation(), ImmutableDictionary.CreateRange(new[]
                {
                    new KeyValuePair<string, string?>("MethodName", method.Name),
                }), method.Name, symbol.Name));
            }


        }
    }

    private static T GetAttributeValue<T>(object attribute, object indexOrString, SyntaxNodeAnalysisContext context)
    {
        _logger.LogInformation($"[GetAttributeValue] Getting attribute value. Attribute: {attribute}, IndexOrString: {indexOrString}");

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

            _logger.LogInformation($"[GetAttributeValue] AttributeData: {attributeData}, Argument: {argument}");

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
                _logger.LogInformation($"[GetAttributeValue] Type Full Name: {typeFullName}");
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

            _logger.LogInformation($"[GetAttributeValue] AttributeSyntax: {attributeSyntax}, Argument: {argument}");

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
                    _logger.LogInformation($"[GetAttributeValue] Found symbol: {symbol}");
                    _logger.LogInformation($"[GetAttributeValue] Type Name: {typeName}");

                    string typeFullName = $"{symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}, {symbol.ContainingAssembly.Name}";
                    var typeVal = Type.GetType(typeFullName, true);
                    _logger.LogInformation($"[GetAttributeValue] Type Value: {typeVal}");
                    return (T)(object)typeVal;

                }
            }
        }

        return default!;
    }

    private static bool CheckIfNeedsPlug(object method, SyntaxNodeAnalysisContext context)
    {
        if (method is IMethodSymbol methodSymbol)
        {
            _logger.LogInformation($"[CheckIfNeedsPlug] Checking if method needs plug. Method: {methodSymbol.Name}");
            ImmutableArray<AttributeData> attributes = methodSymbol.GetAttributes();
            _logger.LogInformation($"[CheckIfNeedsPlug] Attributes: {attributes}");
            bool isInternalCall = attributes.Any(x => {
                _logger.LogInformation($"[CheckIfNeedsPlug] Checking if method needs plug. Attribute: {x}");
                _logger.LogInformation($"[CheckIfNeedsPlug] Checking if method needs plug. Attribute Name: {x.AttributeClass?.Name}");
                _logger.LogInformation($"[CheckIfNeedsPlug] Checking if method needs plug. Attribute Value: {GetAttributeValue<MethodImplOptions>(x, 0, context)}");
                return  x.AttributeClass?.Name == "MethodImpl" && GetAttributeValue<MethodImplOptions>(x, 0, context).HasFlag(MethodImplOptions.InternalCall);

                });
            _logger.LogInformation($"[CheckIfNeedsPlug] Is Internal Call: {isInternalCall}");
            bool isDllImport = attributes.Any(x => x.AttributeClass?.Name == "DllImport" || x.AttributeClass?.Name == "LibraryImport");
            _logger.LogInformation($"[CheckIfNeedsPlug] Is Dll Import: {isDllImport}");
            return isInternalCall || isDllImport;
        }
        else if (method is MethodDeclarationSyntax methodDeclaration)
        {
            _logger.LogInformation($"[CheckIfNeedsPlug] Checking if method needs plug. Method: {methodDeclaration.Identifier.Text}");
            IEnumerable<AttributeSyntax> attributes = methodDeclaration.AttributeLists.SelectMany(x => x.Attributes);
            bool isInternalCall = attributes.Any(x => x.Name.ToString() == "MethodImpl" && GetAttributeValue<MethodImplOptions>(x, 0, context).HasFlag(MethodImplOptions.InternalCall));
            return isInternalCall || attributes.Any(x => x.Name.ToString() == "DllImport" || x.Name.ToString() == "LibraryImport");
        }
        return false;
    }
}
