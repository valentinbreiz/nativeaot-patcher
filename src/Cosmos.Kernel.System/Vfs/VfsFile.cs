// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.HAL.Vfs;

namespace Cosmos.Kernel.System.Vfs;

/// <summary>
/// Common surface for VFS nodes (files or directories).
/// </summary>
public interface IVfsNodeHandle
{
    string Name { get; }

    IVfsInode Inode { get; }

    bool TryStat(out VfsStat stat);
}

/// <summary>
/// Managed handle for an open file with position and byte I/O.
/// </summary>
public interface IVfsFileHandle : IVfsNodeHandle, IDisposable
{
    long Position { get; }

    long Read(Span<byte> buffer);

    long Write(ReadOnlySpan<byte> buffer);

    bool TrySeek(long offset, SeekWhence whence);

    bool Flush();
}

/// <summary>
/// Default implementation of an open file handle backed by HAL VFS operations.
/// </summary>
internal sealed class VfsFileHandle : IVfsFileHandle
{
    private readonly IVfsOpenFile _openFile;
    private bool _disposed;

    public VfsFileHandle(string name, IVfsInode inode, IVfsOpenFile openFile)
    {
        Name = name;
        Inode = inode;
        _openFile = openFile;
        _disposed = false;
    }

    public string Name { get; }

    public IVfsInode Inode { get; }

    /// <summary>Full path this handle was opened with; only set (and the handle only
    /// registered with <see cref="VfsManager"/>) on the <see cref="VfsManager.TryOpenFile"/>
    /// path — lookup-produced handles are metadata accessors and stay untracked.</summary>
    internal string? OpenedPath { get; set; }

    /// <summary>Full path of a directory entry to remove once the last open handle on
    /// this node closes — delete-pending, because FAT-style drivers free the
    /// data clusters immediately on unlink while handles still reference them.</summary>
    internal string? PendingUnlinkPath { get; set; }

    internal bool Tracked { get; set; }

    public long Position => _openFile.Position;

    public long Read(Span<byte> buffer)
    {
        EnsureNotDisposed();
        long bytesRead = _openFile.Operations.Read(_openFile, buffer);
        _openFile.Position += bytesRead;
        return bytesRead;
    }

    public long Write(ReadOnlySpan<byte> buffer)
    {
        EnsureNotDisposed();
        long bytesWritten = _openFile.Operations.Write(_openFile, buffer);
        _openFile.Position += bytesWritten;
        return bytesWritten;
    }

    public bool TrySeek(long offset, SeekWhence whence)
    {
        EnsureNotDisposed();
        if (!_openFile.Operations.Seek(_openFile, offset, whence, out long newPosition))
        {
            return false;
        }

        _openFile.Position = newPosition;
        return true;
    }

    public bool Flush()
    {
        EnsureNotDisposed();
        return _openFile.Operations.Fsync(_openFile);
    }

    public bool TryStat(out VfsStat stat)
    {
        return Inode.InodeOperations.GetAttr(Inode, out stat);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _openFile.Operations.Release(_openFile);
        _disposed = true;

        if (Tracked)
        {
            Tracked = false;
            VfsManager.OnOpenFileClosed(this);
        }
    }

    private void EnsureNotDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(VfsFileHandle));
        }
    }
}
