using System.Runtime;
using Cosmos.Boot.Limine;

unsafe class Program
{
    static readonly LimineFramebufferRequest Framebuffer = new();

    [RuntimeExport("kmain")]
    static void Main()
    {
        LimineFramebuffer* fb = Framebuffer.Response->Framebuffers[0];
        uint* addr = (uint*)fb->Address;
        ulong pitch = fb->Pitch;

        for (uint i = 0; i < 100; i++)
        {
            addr[i * (pitch / 4) + i] = 0xffffff;
        }

        while (true) ;
    }
}
