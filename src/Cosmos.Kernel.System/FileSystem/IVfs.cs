// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.System.FileSystem;

/// <summary>
/// Virtual File System interface, similar to Linux's VFS.
/// Provides a unified interface for file operations across different file systems.
/// </summary>
public interface IVfs
{
    /// <summary>
    /// Opens a file and returns a file handle.
    /// </summary>
    /// <param name="path">The path to the file.</param>
    /// <param name="mode">The file access mode (read, write, read/write).</param>
    /// <param name="create">Whether to create the file if it doesn't exist.</param>
    /// <returns>A file handle, or null if the file cannot be opened.</returns>
    IFileHandle? Open(string path, FileAccessMode mode, bool create = false);

    /// <summary>
    /// Closes a file handle.
    /// </summary>
    /// <param name="handle">The file handle to close.</param>
    void Close(FileHandle handle);

    /// <summary>
    /// Reads data from a file handle.
    /// </summary>
    /// <param name="handle">The file handle.</param>
    /// <param name="buffer">Buffer to read into.</param>
    /// <param name="offset">Offset in buffer to start writing.</param>
    /// <param name="count">Number of bytes to read.</param>
    /// <returns>Number of bytes read.</returns>
    int Read(FileHandle handle, byte[] buffer, int offset, int count);

    /// <summary>
    /// Writes data to a file handle.
    /// </summary>
    /// <param name="handle">The file handle.</param>
    /// <param name="buffer">Buffer to write from.</param>
    /// <param name="offset">Offset in buffer to start reading.</param>
    /// <param name="count">Number of bytes to write.</param>
    /// <returns>Number of bytes written.</returns>
    int Write(FileHandle handle, byte[] buffer, int offset, int count);

    /// <summary>
    /// Seeks to a position in a file handle.
    /// </summary>
    /// <param name="handle">The file handle.</param>
    /// <param name="offset">Offset to seek to.</param>
    /// <param name="origin">Origin of the seek operation.</param>
    /// <returns>New position in the file.</returns>
    long Seek(FileHandle handle, long offset, SeekOrigin origin);

    /// <summary>
    /// Gets a stream for a file handle.
    /// </summary>
    /// <param name="handle">The file handle.</param>
    /// <returns>A stream for the file handle.</returns>
    Stream GetStream(FileHandle handle);

    /// <summary>
    /// Creates a new file.
    /// </summary>
    /// <param name="path">The path where to create the file.</param>
    /// <returns>True if the file was created, false otherwise.</returns>
    bool CreateFile(string path);

    /// <summary>
    /// Creates a new directory.
    /// </summary>
    /// <param name="path">The path where to create the directory.</param>
    /// <returns>True if the directory was created, false otherwise.</returns>
    bool CreateDirectory(string path);

    /// <summary>
    /// Deletes a file or directory.
    /// </summary>
    /// <param name="path">The path to delete.</param>
    /// <returns>True if the file/directory was deleted, false otherwise.</returns>
    bool Delete(string path);

    /// <summary>
    /// Renames a file or directory.
    /// </summary>
    /// <param name="oldPath">The old path.</param>
    /// <param name="newPath">The new path.</param>
    /// <returns>True if the rename was successful, false otherwise.</returns>
    bool Rename(string oldPath, string newPath);

    /// <summary>
    /// Checks if a path exists.
    /// </summary>
    /// <param name="path">The path to check.</param>
    /// <returns>True if the path exists, false otherwise.</returns>
    bool Exists(string path);

    /// <summary>
    /// Gets information about a file or directory.
    /// </summary>
    /// <param name="path">The path to get information about.</param>
    /// <returns>The inode for the path, or null if not found.</returns>
    IInode? GetInode(string path);

    /// <summary>
    /// Lists the contents of a directory.
    /// </summary>
    /// <param name="path">The path to the directory.</param>
    /// <returns>List of inodes in the directory, or an empty list if the path is not a directory or doesn't exist.</returns>
    List<IInode> ListDirectory(string path);

    /// <summary>
    /// Mounts a file system at a mount point.
    /// </summary>
    /// <param name="fileSystem">The file system to mount.</param>
    /// <param name="mountPoint">The mount point path.</param>
    void Mount(IFileSystemOperations fileSystem, string mountPoint);

    /// <summary>
    /// Unmounts a file system from a mount point.
    /// </summary>
    /// <param name="mountPoint">The mount point path.</param>
    void Unmount(string mountPoint);
}

/// <summary>
/// File access mode for opening files.
/// </summary>
public enum FileAccessMode
{
    /// <summary>
    /// Read-only access.
    /// </summary>
    Read,

    /// <summary>
    /// Write-only access.
    /// </summary>
    Write,

    /// <summary>
    /// Read and write access.
    /// </summary>
    ReadWrite
}
