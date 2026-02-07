// This code is licensed under MIT license (see LICENSE for details)

using System;
using System.IO;
using System.Threading;

namespace Cosmos.Kernel.System.FileSystem;

/// <summary>
/// Represents a file handle, similar to Linux's struct file (file descriptor table entry).
/// Contains all the data that would be stored in a Linux file descriptor table.
/// </summary>
public class FileHandle : IDisposable
{
    /// <summary>
    /// File descriptor ID (similar to fd number in Linux).
    /// </summary>
    public FileHandleId Id { get; internal init; }

    /// <summary>
    /// File position (f_pos in Linux).
    /// </summary>
    public long Position { get; internal set; }

    /// <summary>
    /// File access mode/flags (f_flags in Linux).
    /// </summary>
    public FileAccessMode AccessMode { get; internal set; }

    /// <summary>
    /// Reference to the inode (f_inode in Linux).
    /// </summary>
    public IInode? Inode { get; internal set; }

    /// <summary>
    /// File operations (f_op in Linux).
    /// </summary>
    internal IFileOperations? FileOperations { get; set; }

    public override int GetHashCode() => Id.GetHashCode();

    public string Path => Inode!.Path;

    public long Length => (long)Inode!.Size;
    public bool CanRead => AccessMode is FileAccessMode.Read or FileAccessMode.ReadWrite;
    public bool CanWrite => AccessMode is FileAccessMode.Write or FileAccessMode.ReadWrite;
    public bool CanSeek => true;

    public int Read(byte[] buffer, int offset, int count)
    {
        if (!CanRead) throw new InvalidOperationException("File handle is not open for reading.");
        if (buffer == null) throw new ArgumentNullException(nameof(buffer));
        if (offset < 0 || offset >= buffer.Length) throw new ArgumentOutOfRangeException(nameof(offset));
        if (count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException(nameof(count));

        int bytesRead = FileOperations!.Read(Inode!, buffer, offset, count, Position);
        Position += bytesRead;
        return bytesRead;
    }

    public void Write(byte[] buffer, int offset, int count)
    {
        if (!CanWrite) throw new InvalidOperationException("File handle is not open for writing.");
        if (buffer == null) throw new ArgumentNullException(nameof(buffer));
        if (offset < 0 || offset >= buffer.Length) throw new ArgumentOutOfRangeException(nameof(offset));
        if (count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException(nameof(count));

        FileOperations!.Write(Inode!, buffer, offset, count, Position);
        Position += count;
    }

    public long Seek(long offset, SeekOrigin origin)
    {
        long newPosition = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => Position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };

        if (newPosition < 0) throw new ArgumentOutOfRangeException(nameof(offset), "Cannot seek before the beginning of the file.");
        if (newPosition > Length) throw new ArgumentOutOfRangeException(nameof(offset), "Cannot seek beyond the end of the file.");

        Position = newPosition;
        return Position;
    }

    public void Flush()
    {
        FileOperations!.Flush(Inode!);
    }

    public void Close()
    {
        // Handle is closed by Vfs when removed from the dictionary
        Flush();
    }

    public void Dispose()
    {
        Vfs.Close(Id);
    }
}
