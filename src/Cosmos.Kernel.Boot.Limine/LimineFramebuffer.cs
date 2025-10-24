using System.Runtime.InteropServices;

namespace Cosmos.Kernel.Boot.Limine;

// Adapted from Azerou.
[StructLayout(LayoutKind.Sequential)]
public readonly struct LimineHHDMRequest()
{
    public readonly LimineID ID = new(0x48dcf1cb8ad2b852, 0x63984e959a98244b);
    public readonly ulong Revision = 0;
    public readonly ulong Offset;
}

[StructLayout(LayoutKind.Sequential)]
public readonly unsafe struct LimineRsdpRequest()
{
    public readonly LimineID ID = new(0xc5e77b6b397e7b43, 0x27637845accdcf3c);
    public readonly ulong Revision = 0;
    public readonly LimineRsdpResponse* Response;
}

[StructLayout(LayoutKind.Sequential)]
public readonly unsafe struct LimineRsdpResponse
{
    public readonly ulong Revision;
    public readonly void* Address;
}

[StructLayout(LayoutKind.Sequential)]
public readonly unsafe struct LimineFramebufferRequest()
{
    public readonly LimineID ID = new(0x9d5827dcd881dd75, 0xa3148604f6fab11b);
    public readonly ulong Revision = 0;
    public readonly LimineFramebufferResponse* Response;
}

[StructLayout(LayoutKind.Sequential)]
public readonly unsafe struct LimineFramebufferResponse
{
    public readonly ulong Revision;
    public readonly ulong FramebufferCount;
    public readonly LimineFramebuffer** Framebuffers;
}

public enum LimineFbMemoryModel : byte
{
    Rgb = 1
}

[StructLayout(LayoutKind.Sequential)]
public readonly unsafe struct LimineFramebuffer
{
    public readonly void* Address;
    public readonly ulong Width;
    public readonly ulong Height;
    public readonly ulong Pitch;
    public readonly ulong BitsPerPixel;
    public readonly LimineFbMemoryModel MemoryModel;
    public readonly byte RedMaskSize;
    public readonly byte RedMaskShift;
    public readonly byte GreenMaskSize;
    public readonly byte GreenMaskShift;
    public readonly byte BlueMaskSize;
    public readonly byte BlueMaskShift;
    private readonly byte p1, p2, p3, p4, p5, p6, p7;
    public readonly ulong EdidSize;
    public readonly void* Edid;
}
