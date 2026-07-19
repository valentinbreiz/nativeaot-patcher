using System.Runtime.InteropServices;

namespace Cosmos.Kernel.Boot.Limine;

/// <summary>
/// Limine Executable Cmdline request.
/// Returns the kernel command line set via <c>cmdline:</c> in limine.conf.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly unsafe struct LimineExecutableCmdlineRequest()
{
    public readonly LimineID ID = new(0x4b161536e598651e, 0xb390ad4a2f1f303a);
    public readonly ulong Revision = 0;
    public readonly LimineExecutableCmdlineResponse* Response;
}

[StructLayout(LayoutKind.Sequential)]
public readonly unsafe struct LimineExecutableCmdlineResponse
{
    public readonly ulong Revision;
    public readonly byte* Cmdline;
}
