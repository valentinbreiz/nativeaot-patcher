namespace Cosmos.API.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public sealed class XSharpMethodAttribute : Attribute
{
    /// <summary>
    /// the name of the label to use
    /// </summary>
    public string? Name { get; set; } = null;

    public Task? Plug { get; set; }
}
