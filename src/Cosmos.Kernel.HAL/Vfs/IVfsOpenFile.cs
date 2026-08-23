// This code is licensed under the BSD 3-Clause license (see LICENSE for details)

namespace Cosmos.Kernel.HAL.Vfs;

/// <summary>
/// Per-open file state (position, etc.).
/// </summary>
public interface IVfsOpenFile
{
    IVfsInode Inode { get; }

    IFileOperations Operations { get; }

    long Position { get; set; }
}
