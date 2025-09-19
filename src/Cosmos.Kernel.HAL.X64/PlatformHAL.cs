using Cosmos.Build.API.Enum;
using Cosmos.Kernel.HAL;

namespace Cosmos.Kernel.HAL.X64;


public static class PlatformHAL
{
    private static IPortIO? _portIO;
    private static ICpuOps? _cpuOps;

    public static IPortIO? PortIO => _portIO;
    public static ICpuOps? CpuOps => _cpuOps;

    public static PlatformArchitecture Architecture => PlatformArchitecture.X64;

    public static string PlatformName => "x86-64";

    public static void Initialize()
    {
        _portIO = new X64PortIO();
        _cpuOps = new X64CpuOps();
    }
}
