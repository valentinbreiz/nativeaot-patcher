// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.System.FileSystem;

/// <summary>
/// File system operations interface, similar to Linux's super_operations.
/// Defines operations that can be performed on a mounted file system.
/// </summary>
public interface IFileSystemOperations
{
    /// <summary>
    /// Gets the root inode of the file system.
    /// </summary>
    /// <returns>The root inode.</returns>
    IInode GetRootInode();

    /// <summary>
    /// Gets the inode operations for this file system.
    /// </summary>
    IInodeOperations InodeOperations { get; }

    /// <summary>
    /// Gets the mount point path.
    /// </summary>
    string MountPoint { get; }

    /// <summary>
    /// Gets the total size of the file system in bytes.
    /// </summary>
    ulong TotalSize { get; }

    /// <summary>
    /// Gets the available free space in bytes.
    /// </summary>
    ulong AvailableFreeSpace { get; }

    /// <summary>
    /// Gets the file system type name.
    /// </summary>
    string FileSystemType { get; }

    /// <summary>
    /// Looks up an inode by path.
    /// </summary>
    /// <param name="path">The path to look up (relative to mount point).</param>
    /// <returns>The inode at the path, or null if not found.</returns>
    IInode? GetInode(string path);

    /// <summary>
    /// Synchronizes all file system data to storage.
    /// </summary>
    void Sync();
}
