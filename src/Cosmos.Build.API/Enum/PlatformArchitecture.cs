namespace Cosmos.Build.API.Enum;

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

