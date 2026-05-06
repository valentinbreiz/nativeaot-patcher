// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.HAL.Vfs;

/// <summary>
/// Bitmask selecting which inode fields apply in <c>setattr</c>.
/// </summary>
[Flags]
public enum SetAttrFlags : uint
{
    None = 0,
    Mode = 1 << 0,
    Uid = 1 << 1,
    Gid = 1 << 2,
    Size = 1 << 3,
    Atime = 1 << 4,
    Mtime = 1 << 5,
    Ctime = 1 << 6,
}
