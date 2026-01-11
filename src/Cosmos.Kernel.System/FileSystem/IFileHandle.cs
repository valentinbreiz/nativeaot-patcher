// This code is licensed under MIT license (see LICENSE for details)

using System.IO;

namespace Cosmos.Kernel.System.FileSystem;

/// <summary>
/// Represents an open file handle in the VFS.
/// Similar to Linux's file descriptor.
/// </summary>
public interface IFileHandle
{
    /// <summary>
    /// Gets the file handle ID.
    /// </summary>
    FileHandle Handle { get; }

    /// <summary>
    /// Gets the path of the file.
    /// </summary>
    string Path { get; }

    /// <summary>
    /// Gets the current position in the file.
    /// </summary>
    long Position { get; set; }

    /// <summary>
    /// Gets the length of the file.
    /// </summary>
    long Length { get; }

    /// <summary>
    /// Gets whether the file is readable.
    /// </summary>
    bool CanRead { get; }

    /// <summary>
    /// Gets whether the file is writable.
    /// </summary>
    bool CanWrite { get; }

    /// <summary>
    /// Gets whether the file supports seeking.
    /// </summary>
    bool CanSeek { get; }

    /// <summary>
    /// Reads data from the file into the buffer.
    /// </summary>
    /// <param name="buffer">Buffer to read into.</param>
    /// <param name="offset">Offset in buffer to start writing.</param>
    /// <param name="count">Number of bytes to read.</param>
    /// <returns>Number of bytes read.</returns>
    int Read(byte[] buffer, int offset, int count);

    /// <summary>
    /// Writes data from the buffer to the file.
    /// </summary>
    /// <param name="buffer">Buffer to write from.</param>
    /// <param name="offset">Offset in buffer to start reading.</param>
    /// <param name="count">Number of bytes to write.</param>
    void Write(byte[] buffer, int offset, int count);

    /// <summary>
    /// Seeks to a position in the file.
    /// </summary>
    /// <param name="offset">Offset to seek to.</param>
    /// <param name="origin">Origin of the seek operation.</param>
    /// <returns>New position in the file.</returns>
    long Seek(long offset, SeekOrigin origin);

    /// <summary>
    /// Flushes any buffered data to the underlying storage.
    /// </summary>
    void Flush();

    /// <summary>
    /// Closes the file handle.
    /// </summary>
    void Close();
}
