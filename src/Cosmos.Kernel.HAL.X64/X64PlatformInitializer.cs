// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Build.API.Enum;
using Cosmos.Kernel.HAL.Interfaces;

namespace Cosmos.Kernel.HAL.X64;

/// <summary>
/// X64 platform initializer - creates x64-specific HAL components.
/// </summary>
public class X64PlatformInitializer : IPlatformInitializer
{
    public string PlatformName => "x86-64";
    public PlatformArchitecture Architecture => PlatformArchitecture.X64;

    public IPortIO CreatePortIO() => new X64PortIO();
    public ICpuOps CreateCpuOps() => new X64CpuOps();
}
