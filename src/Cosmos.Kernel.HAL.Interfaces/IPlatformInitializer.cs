// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Build.API.Enum;

namespace Cosmos.Kernel.HAL.Interfaces;

/// <summary>
/// Interface for platform-specific HAL initialization.
/// Implemented by HAL.X64 and HAL.ARM64.
/// </summary>
public interface IPlatformInitializer
{
    string PlatformName { get; }
    PlatformArchitecture Architecture { get; }
    IPortIO CreatePortIO();
    ICpuOps CreateCpuOps();
}
