using System.Runtime.InteropServices;

namespace Cosmos.Kernel.Boot.Limine;

// Limine base revision marker — must exist in the binary so the bootloader can
// detect which protocol revision the kernel speaks. Limine 11 raised the minimum
// to 6 on aarch64, so absence of the marker (assumed 0) is now a hard error.
// Magic numbers are fixed by the Limine protocol; do not change them.
[StructLayout(LayoutKind.Sequential)]
public readonly struct LimineBaseRevision(ulong revision)
{
    public readonly ulong Magic1 = 0xf9562b2d5c95a6c8;
    public readonly ulong Magic2 = 0x6a7b384944536bdc;
    public readonly ulong Revision = revision;
}

public static class Limine
{
    public static readonly LimineBaseRevision BaseRevision = new(6);
    public static readonly LimineFramebufferRequest Framebuffer = new();
    public static readonly LimineHHDMRequest HHDM = new();
    public static readonly LimineMemmapRequest MemoryMap = new();
    public static readonly LimineRsdpRequest Rsdp = new();
    public static readonly LimineDTBRequest DTB = new();
    public static readonly LimineBootTimeRequest BootTime = new();
    public static readonly LimineEfiSystemTableRequest EfiSystemTable = new();
}
