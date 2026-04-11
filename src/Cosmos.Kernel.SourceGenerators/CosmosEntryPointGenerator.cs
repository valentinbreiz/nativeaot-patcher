using System.CodeDom.Compiler;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Cosmos.Kernel.SourceGenerators;

[Generator]
public class CosmosEntryPointGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {

        // Access project properties
        var projectProperty = context.AnalyzerConfigOptionsProvider
            .Select((options, _) =>
                options.GlobalOptions.TryGetValue("build_property.CosmosKernelClass", out var value)
                    ? value
                    : null);

        // Register only if property is filled
        context.RegisterSourceOutput(projectProperty, (spc, cosmosKernelClass) =>
        {
            if (!string.IsNullOrEmpty(cosmosKernelClass))
            {
                spc.AddSource("CosmosEntryPoint.g.cs", SourceText.From($@"
// Auto-generated
namespace Cosmos.Kernel.System.Internal;

[global::System.CodeDom.Compiler.GeneratedCode(""{typeof(CosmosEntryPointGenerator).FullName}"", ""{typeof(CosmosEntryPointGenerator).Assembly.GetName().Version}"")]
public static class CosmosEntryPoint
{{
    public static void Main()
    {{
        Cosmos.Kernel.System.Global.RegisterKernel(new global::{cosmosKernelClass}());
        Cosmos.Kernel.System.Global.StartKernel();
    }}
}}
", Encoding.UTF8));
            }
        });
    }
}
