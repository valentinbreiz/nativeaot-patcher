using Cosmos.Build.API.Enum;
using Cosmos.Kernel.HAL;
using Cosmos.Kernel.HAL.Pci;
using Cosmos.Kernel.HAL.ARM64;

namespace Cosmos.Kernel.HAL;

public static partial class PlatformHAL
{

    public static partial void Initialize()
    {
        _platformName = "ARM64";
        _architecture = PlatformArchitecture.ARM64;
        _portIO = new ARM64MemoryIO();
        _cpuOps = new ARM64CpuOps();

    }
}
