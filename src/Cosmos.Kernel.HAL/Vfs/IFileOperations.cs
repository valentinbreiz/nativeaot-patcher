// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.HAL.Vfs;

/// <summary>
/// Open-file byte I/O.
/// </summary>
public interface IFileOperations
{
    /// <summary>Bytes read; <c>0</c> indicates end-of-file.</summary>
    long Read(IVfsOpenFile openFile, Span<byte> buffer);

    /// <summary>Bytes written.</summary>
    long Write(IVfsOpenFile openFile, ReadOnlySpan<byte> buffer);

    bool Seek(IVfsOpenFile openFile, long offset, SeekWhence whence, out long newPosition);

    bool Fsync(IVfsOpenFile openFile);

    void Release(IVfsOpenFile openFile);
}
