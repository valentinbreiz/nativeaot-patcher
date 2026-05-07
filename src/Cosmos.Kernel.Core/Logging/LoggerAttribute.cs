// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.Core.Logging;

[AttributeUsage(AttributeTargets.Class)]
public sealed class LoggerAttribute : Attribute
{
    public string? Category { get; set; }
}
