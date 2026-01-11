// This code is licensed under MIT license (see LICENSE for details)

using System;
using System.IO;

namespace Cosmos.Kernel.System.FileSystem;

/// <summary>
/// Concrete implementation of IFileHandle for VFS.
/// </summary>
internal class VfsFileHandle : IFileHandle
{
    private readonly IInode _inode;
    private readonly IFileOperations _fileOperations;
    private readonly FileAccessMode _accessMode;
    private long _position;

    public VfsFileHandle(FileHandle handle, IInode inode, IFileOperations fileOperations, FileAccessMode accessMode)
    {
        Handle = handle;
        _inode = inode ?? throw new ArgumentNullException(nameof(inode));
        _fileOperations = fileOperations ?? throw new ArgumentNullException(nameof(fileOperations));
        _accessMode = accessMode;
        _position = 0;
    }

    public FileHandle Handle { get; }
    public string Path => _inode.Path;
    public long Position
    {
        get => _position;
        set
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            if (value > Length) throw new ArgumentOutOfRangeException(nameof(value));
            _position = value;
        }
    }

    public long Length => (long)_inode.Size;
    public bool CanRead => _accessMode == FileAccessMode.Read || _accessMode == FileAccessMode.ReadWrite;
    public bool CanWrite => _accessMode == FileAccessMode.Write || _accessMode == FileAccessMode.ReadWrite;
    public bool CanSeek => true;

    public int Read(byte[] buffer, int offset, int count)
    {
        if (!CanRead) throw new InvalidOperationException("File handle is not open for reading.");
        if (buffer == null) throw new ArgumentNullException(nameof(buffer));
        if (offset < 0 || offset >= buffer.Length) throw new ArgumentOutOfRangeException(nameof(offset));
        if (count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException(nameof(count));

        int bytesRead = _fileOperations.Read(_inode, buffer, offset, count, _position);
        _position += bytesRead;
        return bytesRead;
    }

    public void Write(byte[] buffer, int offset, int count)
    {
        if (!CanWrite) throw new InvalidOperationException("File handle is not open for writing.");
        if (buffer == null) throw new ArgumentNullException(nameof(buffer));
        if (offset < 0 || offset >= buffer.Length) throw new ArgumentOutOfRangeException(nameof(offset));
        if (count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException(nameof(count));

        _fileOperations.Write(_inode, buffer, offset, count, _position);
        _position += count;
    }

    public long Seek(long offset, SeekOrigin origin)
    {
        long newPosition = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };

        if (newPosition < 0) throw new ArgumentOutOfRangeException(nameof(offset), "Cannot seek before the beginning of the file.");
        if (newPosition > Length) throw new ArgumentOutOfRangeException(nameof(offset), "Cannot seek beyond the end of the file.");

        _position = newPosition;
        return _position;
    }

    public void Flush()
    {
        _fileOperations.Flush(_inode);
    }

    public void Close()
    {
        // Handle is closed by Vfs when removed from the dictionary
        Flush();
    }
}
