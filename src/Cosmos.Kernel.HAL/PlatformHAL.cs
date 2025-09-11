namespace Cosmos.Kernel.HAL;

public enum PlatformArchitecture
{
    X64,
    ARM64,
    RISCV64
}

public static class PlatformHAL
{
    private static IPortIO? _portIO;
    private static ICpuOps? _cpuOps;

    public static IPortIO? PortIO => _portIO;
    public static ICpuOps? CpuOps => _cpuOps;

    public static PlatformArchitecture Architecture =>
#if ARCH_ARM64
        PlatformArchitecture.ARM64;
#else
        PlatformArchitecture.X64;
#endif

    public static string PlatformName =>
#if ARCH_ARM64
        "ARM64/AArch64";
#else
        "x86-64";
#endif

    public static void Initialize()
    {
#if ARCH_ARM64
        _portIO = new ARM64MemoryIO();
        _cpuOps = new ARM64CpuOps();
#else
        _portIO = new X64PortIO();
        _cpuOps = new X64CpuOps();
#endif
    }
}
