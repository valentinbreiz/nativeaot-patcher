using Cosmos.Build.API.Attributes;
using Cosmos.Kernel.HAL;

namespace Cosmos.Kernel.HAL.X64;

[PlatformSpecific(PlatformArchitecture.X64)]
public class X64PlatformHAL : IPlatformHAL
{
    private readonly X64PortIO _portIO;
    private readonly X64CpuOps _cpuOps;
    
    public X64PlatformHAL()
    {
        _portIO = new X64PortIO();
        _cpuOps = new X64CpuOps();
    }
    
    public IPortIO? PortIO => _portIO;
    public ICpuOps CpuOps => _cpuOps;
    public PlatformArchitecture Architecture => PlatformArchitecture.X64;
    public string PlatformName => "x86-64";
    
    public void Initialize()
    {
        // Minimal initialization
    }
}