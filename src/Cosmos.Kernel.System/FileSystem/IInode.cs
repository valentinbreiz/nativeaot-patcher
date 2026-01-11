// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.System.FileSystem;

/// <summary>
/// Represents an inode (index node) in the VFS, similar to Linux's inode.
/// An inode contains metadata about a file or directory.
/// </summary>
public interface IInode
{
    /// <summary>
    /// Gets the inode number (unique identifier within the file system).
    /// </summary>
    ulong InodeNumber { get; }

    /// <summary>
    /// Gets the file system this inode belongs to.
    /// </summary>
    IFileSystemOperations FileSystem { get; }

    /// <summary>
    /// Gets the path of this inode.
    /// </summary>
    string Path { get; }

    /// <summary>
    /// Gets the name of this inode.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the size of the file/directory in bytes.
    /// </summary>
    ulong Size { get; }

    /// <summary>
    /// Gets whether this inode represents a directory.
    /// </summary>
    bool IsDirectory { get; }

    /// <summary>
    /// Gets whether this inode represents a regular file.
    /// </summary>
    bool IsFile { get; }

    /// <summary>
    /// Gets the file operations for this inode.
    /// </summary>
    IFileOperations? FileOperations { get; }

    /// <summary>
    /// Gets the parent inode (null for root).
    /// </summary>
    IInode? Parent { get; }
}
