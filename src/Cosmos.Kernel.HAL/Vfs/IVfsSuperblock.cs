// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.HAL.Vfs;

/// <summary>
/// Per-mount filesystem instance.
/// </summary>
public interface IVfsSuperblock
{
    IVfsInode Root { get; }

    ISuperblockOperations SuperOperations { get; }

    /// <summary>Fundamental block size in bytes, or 0 if not applicable.</summary>
    long BlockSize { get; }

    /// <summary>Maximum file name length (bytes).</summary>
    ulong MaxNameLength { get; }
}
