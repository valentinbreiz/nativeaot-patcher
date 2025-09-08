using System;

namespace Cosmos.Build.API.Attributes;

/// <summary>
/// Indicates that a type or member is specific to certain processor architectures.
/// The patcher will filter these elements based on the target architecture during build.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = false)]
public class PlatformSpecificAttribute : Attribute
{
    /// <summary>
    /// The architectures this element supports.
    /// </summary>
    public PlatformArchitecture Architecture { get; }

    /// <summary>
    /// Creates a new PlatformSpecific attribute.
    /// </summary>
    /// <param name="architecture">The supported architectures (can be combined with OR).</param>
    public PlatformSpecificAttribute(PlatformArchitecture architecture)
    {
        Architecture = architecture;
    }
}

/// <summary>
/// Processor architectures supported by the kernel.
/// </summary>
[Flags]
public enum PlatformArchitecture
{
    /// <summary>
    /// No specific architecture (platform-agnostic).
    /// </summary>
    None = 0,

    /// <summary>
    /// x86-64 / AMD64 architecture.
    /// </summary>
    X64 = 1,

    /// <summary>
    /// ARM64 / AArch64 architecture.
    /// </summary>
    ARM64 = 2,

    /// <summary>
    /// RISC-V 64-bit architecture.
    /// </summary>
    RISCV64 = 4,

    /// <summary>
    /// All supported architectures.
    /// </summary>
    All = X64 | ARM64 | RISCV64
}
