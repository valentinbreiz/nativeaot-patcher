using System;

namespace Cosmos.Kernel.Boot.Limine;

public static class Limine
{
    public static readonly LimineFramebufferRequest Framebuffer = new();
    public static readonly LimineHHDMRequest HHDM = new();
    public static readonly LimineMemmapRequest MemoryMap = new();
}
