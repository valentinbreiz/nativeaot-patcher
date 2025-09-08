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
    private static IPlatformHAL? _platformHAL;

    /// <summary>
    /// Gets the current platform HAL instance, if available.
    /// </summary>
    public static IPlatformHAL? PlatformHAL => _platformHAL;

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
        InitializePlatformHAL();

        Console.WriteLine("CosmosOS gen3 v0.1 booted.");

        // Display architecture info if HAL is available
        if (_platformHAL != null)
        {
            Console.WriteLine($"Architecture: {_platformHAL.Architecture}");
        }

        Serial.ComInit();
        Console.WriteLine("UART started.");
        Serial.WriteString("Hello from UART\n");
    }

    private static void InitializePlatformHAL()
    {
        try
        {
            _platformHAL = PlatformHALFactory.GetPlatformHAL();
            _platformHAL?.Initialize();
        }
        catch
        {
            // HAL is optional - system can run without it
            _platformHAL = null;
        }
    }

    /// <summary>
    /// Halt the CPU if HAL is available, otherwise spin loop.
    /// </summary>
    public static void Halt()
    {
        if (_platformHAL != null)
        {
            _platformHAL.CpuOps.Halt();
        }
        else
        {
            // Fallback to spin loop
            while (true) { }
        }
    }
}
