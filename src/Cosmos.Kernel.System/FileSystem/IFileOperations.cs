// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.System.FileSystem;

/// <summary>
/// File operations interface, similar to Linux's file_operations.
/// Defines operations that can be performed on an open file.
/// </summary>
public interface IFileOperations
{
    /// <summary>
    /// Reads data from a file at the specified position.
    /// </summary>
    /// <param name="inode">The inode representing the file.</param>
    /// <param name="buffer">Buffer to read into.</param>
    /// <param name="offset">Offset in buffer to start writing.</param>
    /// <param name="count">Number of bytes to read.</param>
    /// <param name="position">Position in file to read from.</param>
    /// <returns>Number of bytes read.</returns>
    int Read(IInode inode, byte[] buffer, int offset, int count, long position);

    /// <summary>
    /// Writes data to a file at the specified position.
    /// </summary>
    /// <param name="inode">The inode representing the file.</param>
    /// <param name="buffer">Buffer to write from.</param>
    /// <param name="offset">Offset in buffer to start reading.</param>
    /// <param name="count">Number of bytes to write.</param>
    /// <param name="position">Position in file to write to.</param>
    /// <returns>Number of bytes written.</returns>
    int Write(IInode inode, byte[] buffer, int offset, int count, long position);

    /// <summary>
    /// Flushes any buffered data for a file.
    /// </summary>
    /// <param name="inode">The inode representing the file.</param>
    void Flush(IInode inode);

    /// <summary>
    /// Truncates a file to the specified length.
    /// </summary>
    /// <param name="inode">The inode representing the file.</param>
    /// <param name="length">New length of the file.</param>
    void Truncate(IInode inode, long length);
}
