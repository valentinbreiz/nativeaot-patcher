// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Build.API.Enum;
using Cosmos.Kernel.HAL.Interfaces;

namespace Cosmos.Kernel.HAL;

/// <summary>
/// Platform HAL manager - provides access to platform-specific hardware.
/// </summary>
public static class PlatformHAL
{
    private static IPortIO? _portIO;
    private static ICpuOps? _cpuOps;
    private static PlatformArchitecture _architecture;
    private static string? _platformName;

    public static IPortIO PortIO => _portIO!;
    public static ICpuOps? CpuOps => _cpuOps;
    public static PlatformArchitecture Architecture => _architecture;
    public static string PlatformName => _platformName ?? "Unknown";

    /// <summary>
    /// Initializes the platform HAL using the provided initializer.
    /// </summary>
    /// <param name="initializer">Platform-specific initializer (X64 or ARM64).</param>
    public static void Initialize(IPlatformInitializer initializer)
    {
        _platformName = initializer.PlatformName;
        _architecture = initializer.Architecture;
        _portIO = initializer.CreatePortIO();
        _cpuOps = initializer.CreateCpuOps();
    }
}
