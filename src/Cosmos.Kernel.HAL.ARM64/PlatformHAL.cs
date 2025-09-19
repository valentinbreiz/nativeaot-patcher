using Cosmos.Build.API.Enum;
using Cosmos.Kernel.HAL;

namespace Cosmos.Kernel.HAL.ARM64;


public static class PlatformHAL
{
    private static IPortIO? _portIO;
    private static ICpuOps? _cpuOps;

    public static IPortIO? PortIO => _portIO;
    public static ICpuOps? CpuOps => _cpuOps;

    public static PlatformArchitecture Architecture => PlatformArchitecture.ARM64;

    public static string PlatformName => "ARM64/AArch64";

    public static void Initialize()
    {
        _portIO = new ARM64MemoryIO();
        _cpuOps = new ARM64CpuOps();
    }
}
