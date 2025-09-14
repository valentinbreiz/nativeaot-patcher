using Cosmos.Build.API.Enum;

namespace Cosmos.Build.API.Attributes;

/// <summary>
/// Indicates that a type or member is specific to certain processor architectures.
/// The patcher will filter these elements based on the target architecture during build.
/// </summary>
/// <remarks>
/// Creates a new PlatformSpecific attribute.
/// </remarks>
/// <param name="architecture">The supported architectures (can be combined with OR).</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public class PlatformSpecificAttribute : Attribute
{
    /// <summary>
    /// The architectures this plug supports.
    /// </summary>
    public PlatformArchitecture Architecture { get; set; }

    public PlatformSpecificAttribute(PlatformArchitecture architecture)
    {
        Architecture = architecture;
    }
}
