// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.HAL.Vfs;

/// <summary>
/// Filesystem statistics.
/// </summary>
public struct VfsStatFs
{
    public ulong Type;
    public ulong BlockSize;
    public ulong Blocks;
    public ulong Bfree;
    public ulong Bavail;
    public ulong Files;
    public ulong Ffree;
    public ulong NameMax;
    public ulong Frsize;
}
