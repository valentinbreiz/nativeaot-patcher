// This code is licensed under MIT license (see LICENSE for details)

using System.Threading;

namespace Cosmos.Kernel.System.FileSystem;

/// <summary>
/// Represents a file handle, similar to Linux's struct file (file descriptor table entry).
/// Contains all the data that would be stored in a Linux file descriptor table.
/// </summary>
public class FileHandle
{
    /// <summary>
    /// File descriptor ID (similar to fd number in Linux).
    /// </summary>
    public uint Id { get; internal init; }

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

    /// <summary>
    /// Reference count (f_count in Linux).
    /// </summary>
    private int _referenceCount;

    /// <summary>
    /// Gets the current reference count.
    /// </summary>
    public int ReferenceCount => _referenceCount;

    /// <summary>
    /// Increments the reference count.
    /// </summary>
    internal void IncrementReference()
    {
        Interlocked.Increment(ref _referenceCount);
    }

    /// <summary>
    /// Decrements the reference count.
    /// </summary>
    /// <returns>The new reference count.</returns>
    internal int DecrementReference()
    {
        return Interlocked.Decrement(ref _referenceCount);
    }

    public override int GetHashCode() => Id.GetHashCode();
}
