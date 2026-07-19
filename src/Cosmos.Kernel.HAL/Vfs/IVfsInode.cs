// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.HAL.Vfs;

/// <summary>
/// Single inode (any type: file, directory, symlink, etc.).
/// </summary>
public interface IVfsInode
{
    IInodeOperations InodeOperations { get; }

    /// <summary>Non-null for regular files and other seekable/readable nodes; null for pure directory entries if the driver splits roles.</summary>
    IFileOperations? FileOperations { get; }

    /// <summary>Leaf name within the parent directory. Empty for the filesystem root.</summary>
    string Name { get; }
}
