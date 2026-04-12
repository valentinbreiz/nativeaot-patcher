using System.Runtime.InteropServices;

namespace Cosmos.Kernel.Boot.Limine;

[StructLayout(LayoutKind.Sequential)]
public readonly unsafe struct LimineExecutableAddressRequest()
{
    public readonly LimineID ID = new(0x71ba76863cc55f63, 0xb2644a48c516a487);
    public readonly ulong Revision = 0;
    public readonly LimineExecutableAddressResponse* Response;
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct LimineExecutableAddressResponse
{
    public readonly ulong Revision;
    public readonly ulong PhysicalBase;
    public readonly ulong VirtualBase;
}
