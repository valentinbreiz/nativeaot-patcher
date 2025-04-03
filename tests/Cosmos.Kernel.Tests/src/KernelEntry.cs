using System.Runtime;
using Cosmos.Boot.Limine;

unsafe class Program
{
    static readonly LimineFramebufferRequest Framebuffer = new();

    [RuntimeExport("kmain")]
    static void Main()
    {
        var fb = Framebuffer.Response->Framebuffers[0];
        var addr = (uint*)fb->Address;
        var pitch = fb->Pitch;

        for (uint i = 0; i < 100; i++) {
            addr[i * (pitch / 4) + i] = 0xffffff;
        }

        while (true) ;
    }
}
