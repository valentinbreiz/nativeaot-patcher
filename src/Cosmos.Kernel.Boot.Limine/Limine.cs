using System.Runtime.InteropServices;

namespace Cosmos.Kernel.Boot.Limine;

/// <summary>
/// Limine protocol base revision marker.
///
/// Limine scans the loaded kernel image for the 3-ulong pattern
/// <c>{0xf9562b2d5c95a6c8, 0x6a7b384944536bdc, &lt;revision&gt;}</c> before
/// handing control to the kernel. NativeAOT preinitializes <c>static readonly</c>
/// struct fields whose initializers are compile-time constants into
/// <c>.rodata</c>, so this declaration is sufficient — the bytes are in the
/// binary at compile time, no C-side marker needed.
///
/// Cosmos declares revision 6 (Limine 11.x — hard-required on aarch64 and the
/// newest revision on x86_64). Revisions ≥1 drop the lower-half identity map
/// and revisions ≥4 exclude RESERVED regions from the HHDM, so all MMIO
/// access routes through <see cref="Cosmos.Kernel.Core.Memory.DeviceMapper"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct LimineBaseRevision()
{
    public readonly ulong Magic1 = 0xf9562b2d5c95a6c8;
    public readonly ulong Magic2 = 0x6a7b384944536bdc;
    public readonly ulong Revision = 6;
}

public static class Limine
{
    public static readonly LimineBaseRevision BaseRevision = new();
    public static readonly LimineFramebufferRequest Framebuffer = new();
    public static readonly LimineHHDMRequest HHDM = new();
    public static readonly LimineMemmapRequest MemoryMap = new();
    public static readonly LimineRsdpRequest Rsdp = new();
    public static readonly LimineDTBRequest DTB = new();
    public static readonly LimineBootTimeRequest BootTime = new();
    public static readonly LimineEfiSystemTableRequest EfiSystemTable = new();
    public static readonly LimineExecutableAddressRequest ExecutableAddress = new();
}
