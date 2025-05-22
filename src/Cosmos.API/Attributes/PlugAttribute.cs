namespace Cosmos.API.Attributes;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PlugAttribute(string targetName, bool isOptional = false, bool replaceTarget = false) : Attribute
{
    //  public TargetPlatform TargetPlatform;

    /// <summary>
    /// does not have a base type
    /// </summary>

    public PlugAttribute() : this(string.Empty)
    {
    }

    /// <summary>
    /// set base type by type
    /// </summary>
    /// <param name="target"></param>
    /// <exception cref="ArgumentNullException"></exception>
    public PlugAttribute(Type target) : this(target.FullName)
    {
    }

    public PlugAttribute(bool replaceTarget) : this(string.Empty, replaceTarget)
    {
    }

    public PlugAttribute(bool isOptional = false, bool replaceTarget = false) : this(string.Empty, isOptional,
        replaceTarget)
    {
    }

    /// <summary>
    /// the type as a string
    /// </summary>
    public string? TargetName { get; set; } = targetName;

    /// <summary>
    /// if the type cant be found skip
    /// </summary>
    public bool IsOptional { get; set; } = isOptional;

    public bool ReplaceTarget { get; set; } = replaceTarget;
}
