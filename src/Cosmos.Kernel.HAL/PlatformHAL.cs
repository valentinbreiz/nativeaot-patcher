using Cosmos.Build.API.Attributes;

namespace Cosmos.Kernel.HAL;

public static class PlatformHAL
{
    private static IPortIO? _portIO;
    private static ICpuOps? _cpuOps;

    public static IPortIO? PortIO => _portIO;
    public static ICpuOps? CpuOps => _cpuOps;

    public static PlatformArchitecture Architecture =>
#if ARM64
        PlatformArchitecture.ARM64;
#else
        PlatformArchitecture.X64;
#endif

    public static string PlatformName =>
#if ARM64
        "ARM64/AArch64";
#else
        "x86-64";
#endif

    public static void Initialize()
    {
#if ARM64
        _portIO = new ARM64MemoryIO();
        _cpuOps = new ARM64CpuOps();
#else
        _portIO = new X64PortIO();
        _cpuOps = new X64CpuOps();
#endif
    }
}
