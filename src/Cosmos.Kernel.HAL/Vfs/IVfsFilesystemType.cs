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
}
