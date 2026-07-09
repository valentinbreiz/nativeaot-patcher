// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.HAL.Vfs;

/// <summary>
/// Registered filesystem driver entry point.
/// </summary>
public interface IVfsFilesystemType
{
    /// <param name="source">Optional backing store identifier (device path, image id, etc.).</param>
    /// <param name="flags">Mount flags (<see cref="MountFlags"/>).</param>
    /// <param name="superblock">Populated superblock on success.</param>
    bool TryMount(ReadOnlySpan<char> source, MountFlags flags, out IVfsSuperblock? superblock);

    /// <summary>
    /// Lay down a fresh on-disk filesystem on the backing store identified by
    /// <paramref name="source"/>. Drivers cast <paramref name="options"/> to
    /// their own concrete <see cref="IVfsFormatOptions"/> type; passing
    /// <c>null</c> selects driver defaults. The source must not be mounted:
    /// <c>VfsManager.TryFormat</c>/<c>TryDestroy</c> refuse a source with a
    /// live mount, since rewriting the volume underneath a superblock's
    /// cached geometry corrupts it.
    /// </summary>
    bool TryFormat(ReadOnlySpan<char> source, IVfsFormatOptions? options);

    /// <summary>
    /// Wipe the filesystem signature on the backing store so it no longer
    /// mounts. The underlying device is not zeroed in full.
    /// </summary>
    bool TryDestroy(ReadOnlySpan<char> source);
}
