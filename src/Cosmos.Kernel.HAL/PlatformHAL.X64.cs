using Cosmos.Build.API.Enum;
using Cosmos.Kernel.HAL;
using Cosmos.Kernel.HAL.Pci;
using Cosmos.Kernel.HAL.X64;

namespace Cosmos.Kernel.HAL;

public static partial class PlatformHAL
{

    public static partial void Initialize()
    {
        _platformName = "x86-64";
        _architecture = PlatformArchitecture.X64;
        _portIO = new X64PortIO();
        _cpuOps = new X64CpuOps();

    }
}
