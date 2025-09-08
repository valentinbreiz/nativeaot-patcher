using System;
using System.Runtime.InteropServices;
using Cosmos.Build.API.Attributes;

namespace Cosmos.Kernel.HAL;

public static class PlatformHALFactory
{
    private static IPlatformHAL? _instance;
    
    public static IPlatformHAL GetPlatformHAL()
    {
        if (_instance != null)
            return _instance;
        
        var arch = GetCurrentArchitecture();
        
        _instance = arch switch
        {
            PlatformArchitecture.X64 => CreateX64HAL(),
            PlatformArchitecture.ARM64 => CreateARM64HAL(),
            PlatformArchitecture.RISCV64 => CreateRISCV64HAL(),
            _ => throw new NotSupportedException($"Architecture {arch} is not supported")
        };
        
        return _instance;
    }
    
    private static PlatformArchitecture GetCurrentArchitecture()
    {
        // Runtime detection based on processor architecture
        var arch = RuntimeInformation.ProcessArchitecture;
        
        return arch switch
        {
            Architecture.X64 => PlatformArchitecture.X64,
            Architecture.Arm64 => PlatformArchitecture.ARM64,
            Architecture.RiscV64 => PlatformArchitecture.RISCV64,
            _ => PlatformArchitecture.None
        };
    }
    
    private static IPlatformHAL CreateX64HAL()
    {
        // Use reflection to load x64 implementation
        var type = Type.GetType("Cosmos.Kernel.HAL.X64.X64PlatformHAL, Cosmos.Kernel.HAL.X64");
        if (type == null)
            throw new InvalidOperationException("X64 HAL implementation not found");
        
        return (IPlatformHAL)Activator.CreateInstance(type)!;
    }
    
    private static IPlatformHAL CreateARM64HAL()
    {
        // Use reflection to load ARM64 implementation
        var type = Type.GetType("Cosmos.Kernel.HAL.ARM64.ARM64PlatformHAL, Cosmos.Kernel.HAL.ARM64");
        if (type == null)
            throw new InvalidOperationException("ARM64 HAL implementation not found");
        
        return (IPlatformHAL)Activator.CreateInstance(type)!;
    }
    
    private static IPlatformHAL CreateRISCV64HAL()
    {
        // Use reflection to load RISC-V implementation
        var type = Type.GetType("Cosmos.Kernel.HAL.RISCV64.RISCV64PlatformHAL, Cosmos.Kernel.HAL.RISCV64");
        if (type == null)
            throw new InvalidOperationException("RISC-V64 HAL implementation not found");
        
        return (IPlatformHAL)Activator.CreateInstance(type)!;
    }
}