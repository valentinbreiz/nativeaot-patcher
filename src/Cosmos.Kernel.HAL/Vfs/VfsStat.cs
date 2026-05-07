// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.HAL.Vfs;

/// <summary>
/// Inode attributes for <c>getattr</c> / <c>stat</c>.
/// </summary>
public struct VfsStat
{
    public ulong Ino;
    public ModeEnum Mode;
    public uint NLink;
    public uint Uid;
    public uint Gid;
    public ulong Rdev;
    public ulong Size;
    public long BlkSize;
    public ulong Blocks;
    public VfsTimespec Atime;
    public VfsTimespec Mtime;
    public VfsTimespec Ctime;
}
