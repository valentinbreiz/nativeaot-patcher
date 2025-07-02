// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Build.API.Attributes;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
public class PlugMemberAttribute(string targetName) : Attribute
{
    public string TargetName { get; set; } = targetName;

    public PlugMemberAttribute() : this(string.Empty) { }
}

