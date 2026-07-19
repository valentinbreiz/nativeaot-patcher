using System.Runtime.InteropServices;

namespace Cosmos.Kernel.Boot.Limine;

/// <summary>
/// Limine Boot Time request.
/// Provides the Unix timestamp (seconds since 1970-01-01 UTC) at the time of boot.
/// Available on all platforms where Limine is the bootloader.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly unsafe struct LimineBootTimeRequest()
{
    public readonly LimineID ID = new(0x502746e184c088aa, 0xfbc5ec83e6327893);
    public readonly ulong Revision = 0;
    public readonly LimineBootTimeResponse* Response;
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct LimineBootTimeResponse
{
    public readonly ulong Revision;
    /// <summary>Unix timestamp in seconds at boot time (UTC).</summary>
    public readonly long BootTime;
}
