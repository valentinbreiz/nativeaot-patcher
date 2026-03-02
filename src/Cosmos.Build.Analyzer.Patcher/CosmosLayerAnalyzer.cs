using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Cosmos.Build.Analyzer.Patcher;

/// <summary>
/// Enforces Cosmos architectural layer rules based on the &lt;CosmosLayer&gt; MSBuild property.
/// The CosmosLayerGenerator source generator embeds [assembly: CosmosLayerAttribute("...")] into
/// each project, and this analyzer reads it from both the current and referenced assemblies.
///
/// Layer order (lowest → highest privilege):
///   Core = 0, HAL = 1, System = 2, User = 3
///
/// Rules:
///   • Projects WITH &lt;CosmosLayer&gt; (bridge projects): may reference the same layer
///     OR exactly one lower layer.
///   • Projects WITHOUT &lt;CosmosLayer&gt; (implicit User): may reference only System bridges
///     (assemblies that explicitly declare CosmosLayer=System).
///   • Referenced assemblies that declare no CosmosLayer are outside the layer system and
///     are never flagged.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CosmosLayerAnalyzer : DiagnosticAnalyzer
{
    private static readonly Dictionary<string, int> s_layerOrder =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "Core",   0 },
            { "HAL",    1 },
            { "System", 2 },
            { "User",   3 },
        };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticMessages.LayerViolation);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        // Read the current project's layer from the assembly attribute (injected via <AssemblyAttribute>
        // in Directory.Build.props when <CosmosLayer> is set).
        string? currentLayerName = GetAssemblyLayer(context.Compilation.Assembly);
        bool hasCurrentLayer = currentLayerName != null;

        // "Any" is a special layer for plug projects that bridge all layers (e.g. Cosmos.Kernel.Plugs).
        // They are exempt from all layer checks.
        if (string.Equals(currentLayerName, "Any", StringComparison.OrdinalIgnoreCase))
            return;

        // Determine the numeric value of the current layer (defaults to User=3 when absent).
        int currentLayerValue = 3;
        if (hasCurrentLayer && !s_layerOrder.TryGetValue(currentLayerName!, out currentLayerValue))
            return; // Unknown layer value — skip to avoid false positives.

        foreach (MetadataReference reference in context.Compilation.References)
        {
            if (context.Compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol refAssembly)
                continue;

            string? refLayerName = GetAssemblyLayer(refAssembly);

            // No CosmosLayer on the referenced assembly → outside the layer system, skip.
            if (refLayerName == null)
                continue;

            // "Any"-layer assemblies (plug projects) may be referenced from any layer without restriction.
            if (string.Equals(refLayerName, "Any", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!s_layerOrder.TryGetValue(refLayerName, out int refLayerValue))
                continue; // Unknown referenced layer — skip.

            bool isAllowed;

            if (!hasCurrentLayer)
            {
                // Implicit-User project: may only reference System bridges.
                isAllowed = string.Equals(refLayerName, "System", StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                // Bridge rule: same layer OR exactly one lower layer.
                isAllowed = refLayerValue >= currentLayerValue - 1
                         && refLayerValue <= currentLayerValue;
            }

            if (!isAllowed)
            {
                string displayCurrentLayer = hasCurrentLayer ? currentLayerName! : "User (implicit)";
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticMessages.LayerViolation,
                    Location.None,
                    refAssembly.Name,
                    refLayerName,
                    displayCurrentLayer));
            }
        }
    }

    /// <summary>Reads CosmosLayerAttribute from an assembly symbol. Returns null if absent.</summary>
    private static string? GetAssemblyLayer(IAssemblySymbol assembly)
    {
        foreach (AttributeData attr in assembly.GetAttributes())
        {
            if (attr.AttributeClass?.Name == "CosmosLayerAttribute"
                && attr.ConstructorArguments.Length > 0
                && attr.ConstructorArguments[0].Value is string layer)
            {
                return layer;
            }
        }
        return null;
    }
}
