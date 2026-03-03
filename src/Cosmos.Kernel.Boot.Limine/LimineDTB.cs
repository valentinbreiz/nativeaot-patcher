using System.Runtime.InteropServices;

namespace Cosmos.Kernel.Boot.Limine;

/// <summary>
/// Limine Device Tree Blob (DTB) request.
/// Provides the address of the flattened device tree passed by firmware.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly unsafe struct LimineDTBRequest()
{
    public readonly LimineID ID = new(0xb40ddb48fb54bac7, 0x545081493f81ffb7);
    public readonly ulong Revision = 0;
    public readonly LimineDTBResponse* Response;
}

[StructLayout(LayoutKind.Sequential)]
public readonly unsafe struct LimineDTBResponse
{
    public readonly ulong Revision;
    public readonly void* Address;
}
