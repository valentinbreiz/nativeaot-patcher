using Cosmos.Build.API.Attributes;

namespace Cosmos.Kernel.HAL;

/// <summary>
/// Minimal platform HAL interface for multi-architecture support
/// </summary>
public interface IPlatformHAL
{
    IPortIO? PortIO { get; }
    ICpuOps CpuOps { get; }
    PlatformArchitecture Architecture { get; }
    string PlatformName { get; }
    void Initialize();
}
