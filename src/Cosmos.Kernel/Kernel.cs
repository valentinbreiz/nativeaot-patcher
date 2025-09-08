using System;
using System.Runtime.InteropServices;
using Cosmos.Build.API.Attributes;
using Cosmos.Kernel.Boot.Limine;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.HAL;
using Cosmos.Kernel.System.IO;

namespace Cosmos.Kernel;

public class Kernel
{
    private static readonly LimineFramebufferRequest Framebuffer = new();

    private static readonly LimineHHDMRequest HHDM = new();

    /// <summary>
    /// Gets the current platform HAL, if available.
    /// </summary>
    public static PlatformArchitecture Architecture => PlatformHAL.Architecture;

    [UnmanagedCallersOnly(EntryPoint = "__Initialize_Kernel")]
    public static unsafe void Initialize()
    {
        MemoryOp.InitializeHeap(HHDM.Offset, 0x1000000);

        // Initialize framebuffer if available
        if (Framebuffer.Response != null && Framebuffer.Response->FramebufferCount > 0)
        {
            LimineFramebuffer* fb = Framebuffer.Response->Framebuffers[0];
            Screen.Init(fb->Address, (uint)fb->Width, (uint)fb->Height, (uint)fb->Pitch);
        }

        // Initialize platform HAL
        PlatformHAL.Initialize();

        Console.WriteLine("CosmosOS gen3 v0.1.2 booted.");

        if (PlatformHAL.Architecture == PlatformArchitecture.X64)
        {
            Console.WriteLine("Architecture: x86-64.");
        }
        else if (PlatformHAL.Architecture == PlatformArchitecture.ARM64)
        {
            Console.WriteLine("Architecture: ARM64/AArch64.");
        }
        else
        {
            Console.WriteLine("Architecture: Unknown.");
        }

        Serial.ComInit();
        Console.WriteLine("UART started.");
        Serial.WriteString("Hello from UART\n");
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
