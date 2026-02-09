// This code is licensed under MIT license (see LICENSE for details)

using System;
using System.IO;

namespace Cosmos.Kernel.System.FileSystem;

/// <summary>
/// Stream wrapper for IFileHandle, allowing file handles to be used as Streams.
/// </summary>
public class FileHandleStream : Stream
{
    private readonly FileHandle _fileHandle;
    private bool _disposed;

    public FileHandleStream(FileHandle? fileHandle)
    {
        _fileHandle = fileHandle ?? throw new ArgumentNullException(nameof(fileHandle));

    }

    public override bool CanRead => _fileHandle.CanRead;
    public override bool CanWrite => _fileHandle.CanWrite;
    public override bool CanSeek => _fileHandle.CanSeek;
    public override long Length => _fileHandle.Length;

    public override long Position
    {
        get => _fileHandle.Position;
        set => Seek(value, SeekOrigin.Current);
    }

    public override void Flush() => _fileHandle.Flush();

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FileHandleStream));
        return _fileHandle.Read(buffer, offset, count);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FileHandleStream));
        return _fileHandle.Seek(offset, origin);
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException("SetLength is not supported on file handles. Use Truncate on the file system instead.");
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FileHandleStream));
        _fileHandle.Write(buffer, offset, count);
    }

    protected override void Dispose(bool disposing)
    {
        _disposed = true;
        base.Dispose(disposing);
    }
}
