using System.Runtime.InteropServices;
using Cosmos.Build.API.Enum;
using Cosmos.Kernel.Boot.Limine;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.HAL;
using Cosmos.Kernel.System.IO;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.Core.Runtime;
#if ARCH_ARM64
using Cosmos.Kernel.HAL.ARM64;
#else
using Cosmos.Kernel.HAL.X64;
#endif

namespace Cosmos.Kernel;

public class Kernel
{

    /// <summary>
    /// Gets the current platform HAL, if available.
    /// </summary>
    public static PlatformArchitecture Architecture => PlatformHAL.Architecture;

    [UnmanagedCallersOnly(EntryPoint = "__Initialize_Kernel")]
    public static unsafe void Initialize()
    {
        // Initialize serial output first for diagnostics
        Serial.ComInit();
        Serial.WriteString("UART started.\n");
        Serial.WriteString("CosmosOS gen3 v0.1.3 booted.\n");

        // Initialize heap for memory allocations
        // Parameters are ignored - heap initialization uses Limine memory map
        MemoryOp.InitializeHeap(0, 0);

        // Initialize platform-specific HAL
        PlatformHAL.Initialize();

        // TODO: Re-enable after fixing stack corruption in module initialization
        // Initialize graphics framebuffer
        // KernelConsole.Initialize();

        if (PlatformHAL.Architecture == PlatformArchitecture.X64)
        {
            Serial.WriteString("Architecture: x86-64.\n");
        }
        else if (PlatformHAL.Architecture == PlatformArchitecture.ARM64)
        {
            Serial.WriteString("Architecture: ARM64/AArch64.\n");
        }
        else
        {
            Serial.WriteString("Architecture: Unknown.\n");
        }

        // Initialize managed modules
        ManagedModule.InitializeModules();
    }

    /// <summary>
    /// Halt the CPU using platform-specific implementation.
    /// </summary>
    public static void Halt()
    {
        if (PlatformHAL.CpuOps != null)
        {
            PlatformHAL.CpuOps.Halt();
        }
        else
        {
            while (true) { }
        }
    }
}
