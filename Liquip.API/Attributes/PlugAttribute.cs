using Liquip.API.Enum;

namespace Liquip.API.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class PlugAttribute : Attribute
{
    
    public TargetPlatform TargetPlatform;

    /// <summary>
    /// does not have a base type
    /// </summary>
    public PlugAttribute()
    {
    }

    /// <summary>
    /// set base type by type
    /// </summary>
    /// <param name="target"></param>
    /// <exception cref="ArgumentNullException"></exception>
    public PlugAttribute(System.Type target)
    {
        this.Target = target ?? throw new ArgumentNullException(nameof (target));
    }

    /// <summary>
    /// set base type by string
    /// </summary>
    /// <param name="targetName"></param>
    /// <exception cref="ArgumentNullException"></exception>
    public PlugAttribute(string targetName)
    {
        this.TargetName = !string.IsNullOrEmpty(targetName) ? targetName : throw new ArgumentNullException(nameof (targetName));
    }

    /// <summary>
    /// the type
    /// </summary>
    public Type? Target { get; set; }

    /// <summary>
    /// the type as a string
    /// </summary>
    public string? TargetName { get; set; }

    /// <summary>
    /// if the type cant be found skip 
    /// </summary>
    public bool IsOptional { get; set; }

}