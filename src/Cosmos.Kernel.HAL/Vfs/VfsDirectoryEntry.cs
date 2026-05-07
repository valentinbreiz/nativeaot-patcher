// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.HAL.Vfs;

/// <summary>
/// Single entry yielded by <see cref="IInodeOperations.ReadDir"/>.
/// </summary>
public readonly struct VfsDirectoryEntry
{
    public string Name { get; }
    public ModeEnum Mode { get; }
    public ulong Size { get; }
    public ulong Ino { get; }

    public VfsDirectoryEntry(string name, ModeEnum mode, ulong size, ulong ino)
    {
        Name = name;
        Mode = mode;
        Size = size;
        Ino = ino;
    }

    public bool IsDirectory => (Mode & ModeEnum.FileTypeMask) == ModeEnum.Directory;
}
