using System;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Boot.Limine;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.HAL;
using Cosmos.Kernel.System.IO;

namespace Cosmos.Kernel;

public class Kernel
{
    private static readonly LimineFramebufferRequest Framebuffer = new();
    private static readonly LimineHHDMRequest HHDM = new();

    [UnmanagedCallersOnly(EntryPoint = "__Initialize_Kernel")]
    public static unsafe void Initialize()
    {
        MemoryOp.InitializeHeap(HHDM.Offset, 0x1000000);
        LimineFramebuffer* fb = Framebuffer.Response->Framebuffers[0];
        Screen.Init(fb->Address, (uint)fb->Width, (uint)fb->Height, (uint)fb->Pitch);
        
        Console.WriteLine("CosmosOS gen3 v0.1 booted.");

        Serial.ComInit();

        Console.WriteLine("UART started.");
        Serial.WriteString("Hello from UART\n");
    }
}
