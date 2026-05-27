
using System;
using System.Collections.Generic;
using System.Linq;
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
        var kernelBuilderProvider = context.SyntaxProvider.ForAttributeWithMetadataName(
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

                var namespaceName = typeSymbol.ContainingNamespace.IsGlobalNamespace
                    ? string.Empty
                    : typeSymbol.ContainingNamespace.ToDisplayString();

                var generatedKernelClassName = GetGeneratedKernelClassName(typeSymbol);
                var typeReference = GetFullyQualifiedTypeReference(typeSymbol);
                var reflectionTypeName = GetReflectionTypeFullName(typeSymbol);

                return new Model(
                    typeSymbol.ToDisplayString(),
                    namespaceName,
                    generatedKernelClassName,
                    typeReference,
                    reflectionTypeName,
                    methods);
            });

        context.RegisterSourceOutput(kernelBuilderProvider, GenerateKernel);
    }

    private static void GenerateKernel(SourceProductionContext context, Model? model)
    {
        if (model is null)
        {
            return;
        }

        var namespaceDeclaration = string.IsNullOrEmpty(model.Namespace)
            ? string.Empty
            : $"namespace {model.Namespace};\n\n";

        var usesInstance = model.Methods.Any(m => !m.IsStatic);
        var sourceBuilder = new StringBuilder();

        sourceBuilder.AppendLine("// Auto-generated");
        sourceBuilder.AppendLine($"[global::System.CodeDom.Compiler.GeneratedCode(\"{typeof(KernelGenerator).FullName}\", \"{typeof(KernelGenerator).Assembly.GetName().Version}\")]");
        sourceBuilder.AppendLine($"public sealed class {model.GeneratedKernelClassName} : global::Cosmos.Kernel.System.Kernel");
        sourceBuilder.AppendLine("{");
        sourceBuilder.AppendLine("    protected override void BeforeRun()");
        sourceBuilder.AppendLine("    {");

        if (usesInstance)
        {
            sourceBuilder.AppendLine($"        var instance = new {model.TypeReference}();");
        }

        sourceBuilder.AppendLine($"        global::Cosmos.TestingFramework.Framework.TestRunner.Start(\"{model.FullyQualifiedName} Tests\", expectedTests: {model.Methods.Count});");

        foreach (var method in model.Methods)
        {
            var testId = $"{model.ReflectionTypeName}.{method.Name}";
            if (method.IsStatic)
            {
                sourceBuilder.AppendLine($"        global::Cosmos.TestingFramework.Framework.TestRunner.Run(\"{testId}\", () => {model.TypeReference}.{method.Name}());");
            }
            else
            {
                sourceBuilder.AppendLine($"        global::Cosmos.TestingFramework.Framework.TestRunner.Run(\"{testId}\", () => instance.{method.Name}());");
            }
        }

        sourceBuilder.AppendLine("        global::Cosmos.TestingFramework.Framework.TestRunner.Finish();");
        sourceBuilder.AppendLine("    }");
        sourceBuilder.AppendLine();
        sourceBuilder.AppendLine("    protected override void Run() => Stop();");
        sourceBuilder.AppendLine();
        sourceBuilder.AppendLine("    protected override void AfterRun()");
        sourceBuilder.AppendLine("    {");
        sourceBuilder.AppendLine("        global::Cosmos.TestingFramework.Framework.TestRunner.Complete();");
        sourceBuilder.AppendLine("        global::Cosmos.Kernel.System.Power.Halt();");
        sourceBuilder.AppendLine("    }");
        sourceBuilder.AppendLine("}");

        var sourceText = SourceText.From(namespaceDeclaration + sourceBuilder.ToString(), Encoding.UTF8);
        var sanitizedSourceName = SanitizeFileName(model.FullyQualifiedName) + ".Kernel.g.cs";
        context.AddSource(sanitizedSourceName, sourceText);
    }

    private static bool HasAttribute(ISymbol symbol, string attributeFullName)
        => symbol.GetAttributes().Any(attribute =>
            string.Equals(attribute.AttributeClass?.ToDisplayString(), attributeFullName, StringComparison.Ordinal));

    private static string GetGeneratedKernelClassName(INamedTypeSymbol typeSymbol)
    {
        var fullyQualifiedName = SanitizeFileName(typeSymbol.ToDisplayString());
        return $"{fullyQualifiedName}GeneratedKernel";
    }

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

    private static string SanitizeFileName(string input)
        => input.Replace("+", "_").Replace('.', '_').Replace('<', '_').Replace('>', '_').Replace(',', '_').Replace(' ', '_');

    private record MethodModel(string Name, bool IsStatic);

    private record Model(
        string FullyQualifiedName,
        string Namespace,
        string GeneratedKernelClassName,
        string TypeReference,
        string ReflectionTypeName,
        List<MethodModel> Methods);
}
