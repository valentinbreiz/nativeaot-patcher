using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Liquip.Patcher.Analzyer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class PatcherAnalyzer : DiagnosticAnalyzer
{
    public const string AnalzyerDiagnosticId = "NAOT";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => DiagnosticMessages.SupportedDiagnostics;

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzePlugAttribute, SyntaxKind.Attribute);
    }

    private void AnalyzePlugAttribute(SyntaxNodeAnalysisContext context)
    {
        Console.WriteLine("Starting AnalyzePlugAttribute");

        if (context.Node is not AttributeSyntax attribute || attribute.Name.ToString() != "Plug")
        {
            Console.WriteLine("Attribute is not 'Plug'");
            return;
        }

        ClassDeclarationSyntax? plugClass = attribute.FirstAncestorOrSelf<ClassDeclarationSyntax>();
        AnalyzePlugClass(plugClass, context);
        Console.WriteLine("Found 'Plug' attribute");

        string targetName = GetAttributeValue<string>(attribute, 0) ?? GetAttributeValue<string>(attribute, "TargetName");
        if (targetName == null)
        {
            Console.WriteLine("TargetName is null");
            return;
        }

        Console.WriteLine($"TargetName: {targetName}");

        string[] statement = targetName.Trim().Split(',');
        string assemblyName = statement.Last();
        string typeName = statement.First();


        Console.WriteLine($"TypeName: {typeName}, AssemblyName: {assemblyName}");

        bool existInAssembly = context.Compilation.GetTypeByMetadataName(typeName) != null || context.Compilation.ReferencedAssemblyNames.Any(x => x.Name == assemblyName) || context.Compilation.ExternalReferences.Any(x => x.Display.Contains(assemblyName)) || Type.GetType(typeName) != null;

        if (!existInAssembly && !GetAttributeValue<bool>(attribute, "IsOptional"))
        {
            Console.WriteLine($"Type {typeName} not found and is not optional");
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticMessages.TypeNotFound, attribute.GetLocation(), typeName));
        }
        // If the type is found, analyze the plugged class
        AnalyzePluggedClass(context.Compilation.GetTypeByMetadataName(typeName), plugClass, context);
        Console.WriteLine("Finished AnalyzePlugAttribute");
    }

    private void AnalyzePlugClass(ClassDeclarationSyntax? classDeclarationSyntax, SyntaxNodeAnalysisContext context)
    {
        if (classDeclarationSyntax == null || classDeclarationSyntax.Modifiers.Any(SyntaxKind.StaticKeyword))
            return;

        context.ReportDiagnostic(Diagnostic.Create(DiagnosticMessages.PlugNotStatic, classDeclarationSyntax.GetLocation(), classDeclarationSyntax.Identifier.Text));
    }

    private void AnalyzePluggedClass(INamedTypeSymbol? symbol, ClassDeclarationSyntax? plugClass, SyntaxNodeAnalysisContext context)
    {
        if (plugClass == null || symbol == null)
        {
            Console.WriteLine("Plug class or symbol is null.");
            return;
        }

        Console.WriteLine($"Analyzing plugged class: {symbol.Name}");

        IEnumerable<string> plugMethods = plugClass.Members.OfType<MethodDeclarationSyntax>().Select(x => x.Identifier.Text);
        Console.WriteLine("Plug methods: " + string.Join(", ", plugMethods));

        foreach (IMethodSymbol method in symbol.GetMembers().OfType<IMethodSymbol>().Where(x => x.MethodKind == MethodKind.Ordinary && !plugMethods.Contains(x.Name)))
        {
            Console.WriteLine($"Analyzing method: {method.Name}");
            ImmutableArray<AttributeData> attributes = method.GetAttributes();
            bool isInternalCall = attributes.Any(x => x.AttributeClass?.Name == "MethodImpl" && GetAttributeValue<MethodImplOptions>(x, 0).HasFlag(MethodImplOptions.InternalCall));
            if (isInternalCall || attributes.Any(x => x.AttributeClass?.Name == "DllImport" || x.AttributeClass?.Name == "LibraryImport"))
            {
                Console.WriteLine($"Method {method.Name} needs a plug.");
                context.ReportDiagnostic(Diagnostic.Create(DiagnosticMessages.MethodNeedsPlug, context.Node.GetLocation(), method.Name, symbol.Name));
            }
        }
    }

    private static T GetAttributeValue<T>(AttributeData attribute, object indexOrString)
    {
        if (attribute == null || attribute.ConstructorArguments.Length == 0)
            return default;

        TypedConstant argument = indexOrString switch
        {
            int nameInt when nameInt >= 0 && nameInt < attribute.ConstructorArguments.Length => attribute.ConstructorArguments[nameInt],
            string nameString => attribute.NamedArguments.FirstOrDefault(x => StringComparer.OrdinalIgnoreCase.Equals(x.Key, nameString)).Value,
            _ => default,
        };

        return argument.Value switch
        {
            _ when typeof(T).IsEnum => (T)Enum.Parse(typeof(T), argument.Value.ToString()),
            _ when argument.Value is T value => value,
            _ when typeof(T) == typeof(Type) => (T)(object)Type.GetType(argument.Value.ToString()),
            _ => default,
        };
    }

    private static T GetAttributeValue<T>(AttributeSyntax attribute, object indexOrString)
    {
        if (attribute == null || attribute.ArgumentList == null || attribute.ArgumentList.Arguments.Count == 0)
            return default;

        ExpressionSyntax? argument = indexOrString switch
        {
            int nameInt when nameInt >= 0 && nameInt < attribute.ArgumentList.Arguments.Count => attribute.ArgumentList.Arguments[nameInt].Expression,
            string nameString => attribute.ArgumentList.Arguments.FirstOrDefault(x => x.NameEquals != null && StringComparer.OrdinalIgnoreCase.Equals(x.NameEquals.Name.ToString(), nameString))?.Expression,
            _ => default,
        };
        var valuea = argument switch
        {
            MemberAccessExpressionSyntax memberAccess when typeof(T).IsEnum => (T)Enum.Parse(typeof(T), memberAccess.Name.ToString()),
            LiteralExpressionSyntax literal when literal.Token.Value is T value => value,
            _ when typeof(T) == typeof(Type) => (T)(object)Type.GetType(argument.ToString()),
            _ => default,
        };
        Console.WriteLine($"Argument: {argument}, Value: {valuea}");

        return valuea;
    }
}
