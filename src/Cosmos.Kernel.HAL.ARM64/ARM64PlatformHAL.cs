using Cosmos.Build.API.Attributes;
using Cosmos.Kernel.HAL;

namespace Cosmos.Kernel.HAL.ARM64;

[PlatformSpecific(PlatformArchitecture.ARM64)]
public class ARM64PlatformHAL : IPlatformHAL
{
    private readonly ARM64MemoryIO _memoryIO;
    private readonly ARM64CpuOps _cpuOps;
    
    public ARM64PlatformHAL()
    {
        _memoryIO = new ARM64MemoryIO();
        _cpuOps = new ARM64CpuOps();
    }
    
    public IPortIO? PortIO => _memoryIO; // Memory-mapped I/O adapter
    public ICpuOps CpuOps => _cpuOps;
    public PlatformArchitecture Architecture => PlatformArchitecture.ARM64;
    public string PlatformName => "ARM64/AArch64";
    
    public void Initialize()
    {
        // Minimal initialization
    }
}