using System.Runtime.InteropServices;

// Adapted from Azerou.

namespace Cosmos.Boot.Limine;

[StructLayout(LayoutKind.Sequential)]
public readonly struct LimineID
{
    public readonly ulong One, Two, Three, Four;

    public LimineID(ulong a3, ulong a4)
    {
        this.One = 0xc7b1dd30df4c8b88; // LIMINE_COMMON_MAGIC
        this.Two = 0x0a82e883a194f07b;
        this.Three = a3;
        this.Four = a4;
    }
}
