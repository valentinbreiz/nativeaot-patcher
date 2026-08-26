// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Build.API.Attributes;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method,  Inherited = false)]
public sealed class ExposeAttribute(string targetMember)
{
    public ExposeAttribute() : this(string.Empty) { }

    public string TargetMember { get; set; } = targetMember;
}
