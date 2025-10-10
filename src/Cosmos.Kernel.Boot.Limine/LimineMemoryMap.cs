using System.Runtime.InteropServices;

namespace Cosmos.Kernel.Boot.Limine;

[StructLayout(LayoutKind.Sequential)]
public readonly unsafe struct LimineMemmapRequest()
{
    public readonly LimineID ID = new(0x67cf3d9d378a806f, 0xe304acdfc50c3c62);
    public readonly ulong Revision = 0;
    public readonly LimineMemmapResponse* Response;
}

[StructLayout(LayoutKind.Sequential)]
public readonly unsafe struct LimineMemmapResponse
{
    public readonly ulong Revision;
    public readonly ulong EntryCount;
    public readonly LimineMemmapEntry** Entries;
}

[StructLayout(LayoutKind.Sequential)]
public readonly unsafe struct LimineMemmapEntry
{
    public readonly void* Base;
    public readonly ulong Length;
    public readonly LimineMemmapType Type;
}

public enum LimineMemmapType : ulong
{
    Usable = 0,
    Reserved = 1,
    AcpiReclaimable = 2,
    AcpiNvs = 3,
    BadMemory = 4,
    BootloaderReclaimable = 5,
    KernelAndModules = 6,
    Framebuffer = 7,
}