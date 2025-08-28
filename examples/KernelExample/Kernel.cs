using System;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Boot.Limine;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.HAL;
using Cosmos.Kernel.Runtime;
using Cosmos.Kernel.System.IO;

internal unsafe class Program
{
    private static readonly LimineFramebufferRequest Framebuffer = new();
    private static readonly LimineHHDMRequest HHDM = new();

    [UnmanagedCallersOnly(EntryPoint = "kmain")]
    private static void KernelMain() => Main();

    private static void Main()
    {
        Native.Debug.BreakpointSoft();

        MemoryOp.InitializeHeap(HHDM.Offset, 0x1000000);
        LimineFramebuffer* fb = Framebuffer.Response->Framebuffers[0];
        Screen.Init(fb->Address, (uint)fb->Width, (uint)fb->Height, (uint)fb->Pitch);

        Console.WriteLine("CosmosOS gen3 v0.1 booted.");

        Serial.ComInit();
        Console.WriteLine("UART started.");
        Serial.WriteString("Hello from UART\n");
        
        // Uncomment to use hard breakpoint (must use Continue, not Step Over)
        // Console.WriteLine("Hard breakpoint (use Continue to resume)...");
        // Native.Debug.Breakpoint();  // INT3 - stops execution until Continue
        // Console.WriteLine("Hard breakpoint passed.");

        char[] testChars = new char[] { 'R', 'h', 'p' };
        string testString = new string(testChars);
        Console.WriteLine(testString);
        Serial.WriteString(testString + "\n");

        while (true) ;
    }
}
