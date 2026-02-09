// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.System.FileSystem;

/// <summary>
/// Inode operations interface, similar to Linux's inode_operations.
/// Defines operations that can be performed on inodes (files and directories).
/// </summary>
public interface IInodeOperations
{
    /// <summary>
    /// Looks up a child inode by name in a directory.
    /// </summary>
    /// <param name="parent">The parent directory inode.</param>
    /// <param name="name">The name of the child to look up.</param>
    /// <returns>The child inode, or null if not found.</returns>
    IInode? Lookup(IInode parent, string name);

    /// <summary>
    /// Creates a new file in a directory.
    /// </summary>
    /// <param name="parent">The parent directory inode.</param>
    /// <param name="name">The name of the file to create.</param>
    /// <returns>The newly created file inode.</returns>
    IInode CreateFile(IInode parent, string name);

    /// <summary>
    /// Creates a new directory in a directory.
    /// </summary>
    /// <param name="parent">The parent directory inode.</param>
    /// <param name="name">The name of the directory to create.</param>
    /// <returns>The newly created directory inode.</returns>
    IInode CreateDirectory(IInode parent, string name);

    /// <summary>
    /// Deletes a file or directory.
    /// </summary>
    /// <param name="inode">The inode to delete.</param>
    void Unlink(IInode inode);

    /// <summary>
    /// Renames a file or directory.
    /// </summary>
    /// <param name="oldInode">The inode to rename.</param>
    /// <param name="newParent">The new parent directory.</param>
    /// <param name="newName">The new name.</param>
    void Rename(IInode oldInode, IInode newParent, string newName);

    /// <summary>
    /// Gets the list of child inodes in a directory.
    /// </summary>
    /// <param name="directory">The directory inode.</param>
    /// <returns>List of child inodes.</returns>
    List<IInode> ReadDirectory(IInode directory);

    /// <summary>
    /// Gets the file operations for a file inode.
    /// </summary>
    /// <param name="inode">The file inode.</param>
    /// <returns>File operations, or null if not a file.</returns>
    IFileOperations? GetFileOperations(IInode inode);
}
