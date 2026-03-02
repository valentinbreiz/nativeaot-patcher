namespace Cosmos.Build.API.Attributes;

/// <summary>
/// Declares the architectural layer of this assembly for the Cosmos layer enforcement analyzer (NAOT0007).
/// Applied automatically via &lt;CosmosLayer&gt; MSBuild property — do not apply manually.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class CosmosLayerAttribute : Attribute
{
    public CosmosLayerAttribute(string layer) => Layer = layer;

    /// <summary>The layer name: Core, HAL, System, or User.</summary>
    public string Layer { get; }
}
