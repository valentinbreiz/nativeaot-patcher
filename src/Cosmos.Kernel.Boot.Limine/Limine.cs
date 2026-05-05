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
    public static readonly LimineExecutableCmdlineRequest ExecutableCmdline = new();

    /// <summary>
    /// Pointer to the kernel command line (null-terminated C string) passed
    /// via <c>cmdline:</c> in limine.conf. Returns null if Limine did not
    /// answer the request or the cmdline is unset.
    /// </summary>
    public static unsafe byte* Cmdline
    {
        get
        {
            if (ExecutableCmdline.Response == null)
            {
                return null;
            }
            return ExecutableCmdline.Response->Cmdline;


        }
    }
}
