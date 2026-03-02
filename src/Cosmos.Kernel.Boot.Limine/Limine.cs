namespace Cosmos.Kernel.Boot.Limine;

public static class Limine
{
    public static readonly LimineFramebufferRequest Framebuffer = new();
    public static readonly LimineHHDMRequest HHDM = new();
    public static readonly LimineMemmapRequest MemoryMap = new();
    public static readonly LimineRsdpRequest Rsdp = new();
    public static readonly LimineDTBRequest DTB = new();
    public static readonly LimineBootTimeRequest BootTime = new();
    public static readonly LimineEfiSystemTableRequest EfiSystemTable = new();
}
