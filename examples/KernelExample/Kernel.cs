using System;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Boot.Limine;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.IO;

internal unsafe class Program
{
    private static readonly LimineFramebufferRequest Framebuffer = new();
    private static readonly LimineHHDMRequest HHDM = new();

    [MethodImpl(MethodImplOptions.InternalCall)]
    [RuntimeImport("testGCC")]
    private static extern char* testGCC();

    [UnmanagedCallersOnly(EntryPoint = "kmain")]
    private static void KernelMain() => Main();

    private static void Main()
    {
        MemoryOp.InitializeHeap(HHDM.Offset, 0x1000000);
        LimineFramebuffer* fb = Framebuffer.Response->Framebuffers[0];
        Canvas.Address = (uint*)fb->Address;
        Canvas.Pitch = (uint)fb->Pitch;
        Canvas.Width = (uint)fb->Width;
        Canvas.Height = (uint)fb->Height;

        Canvas.ClearScreen(Color.Black);

        Canvas.DrawString("CosmosOS booted.", 0, 0, Color.White);

        Serial.ComInit();

        Canvas.DrawString("UART started.", 0, 28, Color.White);

        Serial.WriteString("Hello from UART\n");


        char* gccString = testGCC();
        Canvas.DrawString(gccString, 0, 56, Color.White);

        char[] testChars = new char[] { 'R', 'h', 'p' };
        string testString = new string(testChars);
        testString += " string test";
        Canvas.DrawString(testString, 0, 84, Color.White);
        Serial.WriteString(testString + "\n");

        while (true) ;
    }
}
