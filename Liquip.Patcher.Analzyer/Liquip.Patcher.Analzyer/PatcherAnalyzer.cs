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
    public const string DiagnosticId = "JJK0001";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => DiagnosticMessages.SupportedDiagnostics;

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzePlugAttribute, SyntaxKind.Attribute);
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    // Analyze the PlugAttribute
    private void AnalyzePlugAttribute(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not AttributeSyntax attribute || attribute.Name.ToString() != "Plug")
            return;

        string targetName = GetAttributeValue<string>(attribute, 0);
        targetName ??= GetAttributeValue<string>(attribute, "TargetName");

        if (targetName == null)
            return;


        // Get the type from the TargetName
        string[] statement = targetName.Trim().Split(',');
        string assemblyName = statement.Last();
        string typeName = statement.First();

        // Check if the type exists
        var type = Type.GetType(typeName);
        type ??= Type.GetType(typeName + ", " + assemblyName);

        if (type == null && !GetAttributeValue<bool>(attribute, "IsOptional"))
        {
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticMessages.TypeNotFound, attribute.GetLocation(), typeName));
        }

        // Check if the class being plugged is static
        ClassDeclarationSyntax? classDeclaration = attribute.FirstAncestorOrSelf<ClassDeclarationSyntax>();
        if (classDeclaration != null && !classDeclaration.Modifiers.Any(SyntaxKind.StaticKeyword))
        {
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticMessages.ClassNotStatic, classDeclaration.GetLocation(), classDeclaration.Identifier.Text));
        }
        
    }

    private void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        MethodDeclarationSyntax method = (context.Node as MethodDeclarationSyntax)!;
        IMethodSymbol? symbol = context.SemanticModel.GetDeclaredSymbol(method);

        if (symbol == null)
        {
            return;
        }

        bool needsPlug = false;
        IEnumerable<AttributeSyntax> attributes = method.AttributeLists.SelectMany(x => x.Attributes);

        foreach (AttributeSyntax attribute in attributes)
        {
            string name = attribute.Name.ToString();
            if (name == "DllImport" || name == "LibraryImport")
            {
                needsPlug = true;
                break;
            }
            else if (attribute.Name.ToString() == "MethodImpl")
            {
                MethodImplOptions value = GetAttributeValue<MethodImplOptions>(attribute, 0);
                Console.WriteLine(value);
                bool isInternalCall = value.HasFlag(MethodImplOptions.InternalCall);
                if (isInternalCall || (isInternalCall && symbol.IsExtern && symbol.IsStatic)) // Check if the method is an InternalCall or Native method
                {
                    needsPlug = true;
                    break;
                }
            }
        }

        // Report diagnostic if it's a DllImport, Native, or InternalCall method
        if (needsPlug)
        {
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticMessages.MethodNeedsPlug, method.GetLocation(), symbol.Name));
        }
    }
    private static T GetAttributeValue<T>(AttributeSyntax attribute, object indexOrString)
    {
        if (attribute == null || attribute.ArgumentList == null || attribute.ArgumentList.Arguments.Count == 0)
            return default;

        ExpressionSyntax? argument = indexOrString switch
        {
            _ when indexOrString is int nameInt && nameInt >= 0 && nameInt < attribute.ArgumentList.Arguments.Count => attribute.ArgumentList.Arguments[nameInt].Expression,
            _ when indexOrString is string nameString => attribute.ArgumentList.Arguments.FirstOrDefault(x =>
           {
               if (x.NameEquals == null)
                   return false;
               return StringComparer.OrdinalIgnoreCase.Equals(x.NameEquals.Name.ToString(), nameString);
           }).Expression,
            _ => default,
        };

        return argument switch
        {
            _ when typeof(T).IsEnum && argument is MemberAccessExpressionSyntax memberAccess => (T)Enum.Parse(typeof(T), memberAccess.Name.ToString()),
            _ when argument is LiteralExpressionSyntax literal && literal.Token.Value is T value => value,
            _ when typeof(T) == typeof(Type) => (T)(object)Type.GetType(argument.ToString()),
            _ => default,
        };
    }

}
