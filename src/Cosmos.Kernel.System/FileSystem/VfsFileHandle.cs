// This code is licensed under MIT license (see LICENSE for details)

using System;
using System.IO;

namespace Cosmos.Kernel.System.FileSystem;

/// <summary>
/// Concrete implementation of IFileHandle for VFS.
/// Uses data stored in FileHandle (similar to Linux's file descriptor table entry).
/// </summary>
internal class VfsFileHandle : IFileHandle
{
    private readonly FileHandle _handle;

    public VfsFileHandle(FileHandle handle)
    {
        if (handle == null)
            throw new ArgumentNullException(nameof(handle));
        if (handle.Inode == null)
            throw new ArgumentException("FileHandle must have an inode.", nameof(handle));
        if (handle.FileOperations == null)
            throw new ArgumentException("FileHandle must have file operations.", nameof(handle));

        _handle = handle;
        _handle.IncrementReference();
    }

    public FileHandle Handle => _handle;
    public string Path => _handle.Inode!.Path;
    public long Position
    {
        get => _handle.Position;
        set
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            if (value > Length) throw new ArgumentOutOfRangeException(nameof(value));
            _handle.Position = value;
        }
    }

    public long Length => (long)_handle.Inode!.Size;
    public bool CanRead => _handle.AccessMode == FileAccessMode.Read || _handle.AccessMode == FileAccessMode.ReadWrite;
    public bool CanWrite => _handle.AccessMode == FileAccessMode.Write || _handle.AccessMode == FileAccessMode.ReadWrite;
    public bool CanSeek => true;

    public int Read(byte[] buffer, int offset, int count)
    {
        if (!CanRead) throw new InvalidOperationException("File handle is not open for reading.");
        if (buffer == null) throw new ArgumentNullException(nameof(buffer));
        if (offset < 0 || offset >= buffer.Length) throw new ArgumentOutOfRangeException(nameof(offset));
        if (count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException(nameof(count));

        int bytesRead = _handle.FileOperations!.Read(_handle.Inode!, buffer, offset, count, _handle.Position);
        _handle.Position += bytesRead;
        return bytesRead;
    }

    public void Write(byte[] buffer, int offset, int count)
    {
        if (!CanWrite) throw new InvalidOperationException("File handle is not open for writing.");
        if (buffer == null) throw new ArgumentNullException(nameof(buffer));
        if (offset < 0 || offset >= buffer.Length) throw new ArgumentOutOfRangeException(nameof(offset));
        if (count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException(nameof(count));

        _handle.FileOperations!.Write(_handle.Inode!, buffer, offset, count, _handle.Position);
        _handle.Position += count;
    }

    public long Seek(long offset, SeekOrigin origin)
    {
        long newPosition = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _handle.Position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };

        if (newPosition < 0) throw new ArgumentOutOfRangeException(nameof(offset), "Cannot seek before the beginning of the file.");
        if (newPosition > Length) throw new ArgumentOutOfRangeException(nameof(offset), "Cannot seek beyond the end of the file.");

        _handle.Position = newPosition;
        return _handle.Position;
    }

    public void Flush()
    {
        _handle.FileOperations!.Flush(_handle.Inode!);
    }

    public void Close()
    {
        // Handle is closed by Vfs when removed from the dictionary
        Flush();
        _handle.DecrementReference();
    }
}
