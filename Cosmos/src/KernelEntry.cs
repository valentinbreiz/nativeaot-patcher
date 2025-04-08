using System.Runtime;
using Cosmos.Boot.Limine;

using EarlyBird;
using EarlyBird.Conversion;
using EarlyBird.PSF;
using static EarlyBird.Graphics;

unsafe class Program
{
    static readonly LimineFramebufferRequest Framebuffer = new();
    static readonly LimineHHDMRequest HHDM = new();

    [RuntimeExport("kmain")]
    static void Main()
    {
        MemoryOp.InitializeHeap(HHDM.Offset, 0x1000000);
        var fb = Framebuffer.Response->Framebuffers[0];
        Canvas.Address = (uint*)fb->Address;
        Canvas.Pitch = (uint)fb->Pitch;
        Canvas.Width = (uint)fb->Width;
        Canvas.Height = (uint)fb->Height;

        Canvas.ClearScreen(Color.Black);

        Canvas.DrawString("CosmosOS booted.", 0, 0, Color.White);

        while (true);
    }
}
