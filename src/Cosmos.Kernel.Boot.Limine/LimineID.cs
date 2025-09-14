using System.Runtime.InteropServices;

// Adapted from Azerou.

namespace Cosmos.Kernel.Boot.Limine;

[StructLayout(LayoutKind.Sequential)]
public readonly struct LimineID(ulong a3, ulong a4)
{
    public readonly ulong One = 0xc7b1dd30df4c8b88, Two = 0x0a82e883a194f07b, Three = a3, Four = a4;
}
