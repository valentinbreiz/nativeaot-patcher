// This code is licensed under the BSD 3-Clause license (see LICENSE for details)

using Cosmos.Kernel.HAL.Vfs;

namespace Cosmos.Kernel.System.Vfs;

/// <summary>
/// Common surface for VFS nodes (files or directories). Every handle owns
/// driver state and must be released, so the base interface is disposable and
/// both handle kinds work in <c>using</c> blocks.
/// </summary>
public interface IVfsNodeHandle : IDisposable
{
    /// <summary>The name of the node inside its parent directory.</summary>
    string Name { get; }

    /// <summary>The underlying HAL VFS inode.</summary>
    IVfsInode Inode { get; }

    /// <summary>
    /// Reads the node's metadata (size, timestamps, mode).
    /// </summary>
    /// <param name="stat">The node's metadata when the call succeeds.</param>
    /// <returns><see langword="true"/> when the driver produced the metadata.</returns>
    bool TryStat(out VfsStat stat);
}

/// <summary>
/// Managed handle for an open file with position and byte I/O.
/// </summary>
public interface IVfsFileHandle : IVfsNodeHandle
{
    /// <summary>The current byte offset of the file cursor.</summary>
    long Position { get; }

    /// <summary>
    /// Reads bytes at the current position, advancing it by the amount read.
    /// </summary>
    /// <param name="buffer">The destination buffer.</param>
    /// <returns>The number of bytes read; 0 at end of file.</returns>
    long Read(Span<byte> buffer);

    /// <summary>
    /// Writes bytes at the current position, advancing it by the amount written.
    /// </summary>
    /// <param name="buffer">The bytes to write.</param>
    /// <returns>The number of bytes written.</returns>
    long Write(ReadOnlySpan<byte> buffer);

    /// <summary>
    /// Moves the file cursor.
    /// </summary>
    /// <param name="offset">The offset relative to <paramref name="whence"/>.</param>
    /// <param name="whence">The origin the offset is applied from.</param>
    /// <returns><see langword="true"/> when the resulting position is valid.</returns>
    bool TrySeek(long offset, SeekWhence whence);

    /// <summary>
    /// Flushes buffered writes to the underlying device.
    /// </summary>
    /// <returns><see langword="true"/> when the driver flushed successfully.</returns>
    bool TryFlush();

    /// <summary>
    /// Updates the open file's metadata. Setting
    /// <see cref="SetAttrFlags.Size"/> is how a file is truncated or extended.
    /// </summary>
    /// <param name="flags">Which fields of <paramref name="attributes"/> to apply.</param>
    /// <param name="attributes">The new attribute values.</param>
    /// <returns><see langword="true"/> when the driver applied the change.</returns>
    bool TrySetAttr(SetAttrFlags flags, in VfsStat attributes);
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

    public bool TryFlush()
    {
        EnsureNotDisposed();
        return _openFile.Operations.Fsync(_openFile);
    }

    public bool TrySetAttr(SetAttrFlags flags, in VfsStat attributes)
    {
        EnsureNotDisposed();
        return Inode.InodeOperations.SetAttr(Inode, flags, attributes);
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
