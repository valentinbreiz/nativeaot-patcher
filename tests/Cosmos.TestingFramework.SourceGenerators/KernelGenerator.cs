
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Cosmos.TestingFramework.SourceGenerators;

[Generator]
public class KernelGenerator : IIncrementalGenerator
{
    private const string TestClassAttributeName = "Cosmos.TestingFramework.Attributes.TestClassAttribute";
    private const string TestMethodAttributeName = "Cosmos.TestingFramework.Attributes.TestMethodAttribute";
    private const string SkipAttributeName = "Cosmos.TestingFramework.Attributes.SkipAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var testClassProvider = context.SyntaxProvider.ForAttributeWithMetadataName(
            TestClassAttributeName,
            static (node, cancellationToken) => node is ClassDeclarationSyntax,
            static (context, cancellationToken) =>
            {
                var typeSymbol = (INamedTypeSymbol)context.TargetSymbol;

                if (HasAttribute(typeSymbol, SkipAttributeName))
                {
                    return null;
                }

                var methods = typeSymbol.GetMembers()
                    .OfType<IMethodSymbol>()
                    .Where(methodSymbol =>
                        !HasAttribute(methodSymbol, SkipAttributeName) &&
                        HasAttribute(methodSymbol, TestMethodAttributeName) &&
                        methodSymbol.Parameters.Length == 0 &&
                        !methodSymbol.IsGenericMethod)
                    .Select(methodSymbol => new MethodModel(methodSymbol.Name, methodSymbol.IsStatic))
                    .ToList();

                if (methods.Count == 0)
                {
                    return null;
                }

                var typeReference = GetFullyQualifiedTypeReference(typeSymbol);
                var reflectionTypeName = GetReflectionTypeFullName(typeSymbol);

                return new Model(
                    typeSymbol.ToDisplayString(),
                    typeReference,
                    reflectionTypeName,
                    methods);
            })
            .Where(static model => model is not null)
            .Select(static (model, _) => model!);

        context.RegisterSourceOutput(testClassProvider.Collect(), GenerateKernel);
    }

    private static void GenerateKernel(SourceProductionContext context, ImmutableArray<Model> models)
    {
        if (models.IsDefaultOrEmpty)
        {
            return;
        }

        var sourceBuilder = new StringBuilder();
        var totalMethods = models.Sum(static model => model.Methods.Count);

        sourceBuilder.AppendLine("// Auto-generated");
        sourceBuilder.AppendLine($"[global::System.CodeDom.Compiler.GeneratedCode(\"{typeof(KernelGenerator).FullName}\", \"{typeof(KernelGenerator).Assembly.GetName().Version}\")]");
        sourceBuilder.AppendLine($"[global::Cosmos.TestingFramework.Attributes.GeneratedTestKernel({string.Join(", ", models.Select(static model => $"typeof({model.FullyQualifiedName})"))})]");
        sourceBuilder.AppendLine("public sealed class CosmosTestKernel : global::Cosmos.Kernel.System.Kernel");
        sourceBuilder.AppendLine("{");
        sourceBuilder.AppendLine("    protected override void BeforeRun()");
        sourceBuilder.AppendLine("    {");
        sourceBuilder.AppendLine($"        global::Cosmos.TestingFramework.TestRunner.Start(\"Generated Tests\", expectedTests: {totalMethods});");

        foreach (var model in models)
        {
            string? instanceVariableName = null;
            var usesInstance = model.Methods.Any(static method => !method.IsStatic);
            if (usesInstance)
            {
                instanceVariableName = GetInstanceVariableName(model);
                sourceBuilder.AppendLine($"        var {instanceVariableName} = new {model.TypeReference}();");
            }

            foreach (var method in model.Methods)
            {
                var testId = $"{model.ReflectionTypeName}.{method.Name}";
                if (method.IsStatic)
                {
                    sourceBuilder.AppendLine($"        global::Cosmos.TestingFramework.TestRunner.Run(\"{testId}\", () => {model.TypeReference}.{method.Name}());");
                }
                else
                {
                    sourceBuilder.AppendLine($"        global::Cosmos.TestingFramework.TestRunner.Run(\"{testId}\", () => {instanceVariableName}.{method.Name}());");
                }
            }
        }

        sourceBuilder.AppendLine("        global::Cosmos.TestingFramework.TestRunner.Finish();");
        sourceBuilder.AppendLine("    }");
        sourceBuilder.AppendLine();
        sourceBuilder.AppendLine("    protected override void Run() => Stop();");
        sourceBuilder.AppendLine();
        sourceBuilder.AppendLine("    protected override void AfterRun()");
        sourceBuilder.AppendLine("    {");
        sourceBuilder.AppendLine("        global::Cosmos.TestingFramework.TestRunner.Complete();");
        sourceBuilder.AppendLine("        global::Cosmos.Kernel.System.Power.Halt();");
        sourceBuilder.AppendLine("    }");
        sourceBuilder.AppendLine("}");

        var sourceText = SourceText.From(sourceBuilder.ToString(), Encoding.UTF8);
        var sanitizedSourceName = "CosmosGeneratedTestKernel.Kernel.g.cs";
        context.AddSource(sanitizedSourceName, sourceText);
    }

    private static bool HasAttribute(ISymbol symbol, string attributeFullName)
        => symbol.GetAttributes().Any(attribute =>
            string.Equals(attribute.AttributeClass?.ToDisplayString(), attributeFullName, StringComparison.Ordinal));

    private static string GetInstanceVariableName(Model model)
        => $"instance_{SanitizeIdentifier(model.FullyQualifiedName)}";

    private static string GetFullyQualifiedTypeReference(INamedTypeSymbol typeSymbol)
    {
        var fullyQualified = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return fullyQualified.Replace("global::", "global::").Replace("+", ".");
    }

    private static string GetReflectionTypeFullName(INamedTypeSymbol typeSymbol)
    {
        var nameParts = new List<string>();
        for (var current = typeSymbol; current is not null; current = current.ContainingType)
        {
            nameParts.Add(current.MetadataName);
        }

        nameParts.Reverse();

        var typeName = string.Join("+", nameParts);
        return typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? typeName
            : $"{typeSymbol.ContainingNamespace.ToDisplayString()}.{typeName}";
    }
    private static string SanitizeIdentifier(string input)
        => input.Replace("+", "_").Replace('.', '_').Replace('<', '_').Replace('>', '_').Replace(',', '_').Replace(' ', '_');

    private record MethodModel(string Name, bool IsStatic);

    private record Model(
        string FullyQualifiedName,
        string TypeReference,
        string ReflectionTypeName,
        List<MethodModel> Methods);
}
