// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Build.API.Enum;
using Cosmos.Kernel.HAL.Interfaces;

namespace Cosmos.Kernel.HAL;

public static partial class PlatformHAL
{
    private static IPortIO _portIO;
    private static ICpuOps? _cpuOps;

    public static IPortIO PortIO => _portIO;
    public static ICpuOps? CpuOps => _cpuOps;

    private static PlatformArchitecture _architecture;
    public static PlatformArchitecture Architecture => _architecture;

    private static string _platformName;
    public static string PlatformName => _platformName;

    public static partial void Initialize();
}
