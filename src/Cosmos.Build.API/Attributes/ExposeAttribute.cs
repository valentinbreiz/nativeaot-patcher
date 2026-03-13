// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Build.API.Attributes;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Method)]
public class ExposeAttribute(string targetName) : Attribute
{
    public ExposeAttribute() : this(string.Empty)
    {
    }

    public string TargetName { get; set; } = targetName;
}
