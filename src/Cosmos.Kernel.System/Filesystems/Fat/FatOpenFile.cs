// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.HAL.Vfs;

namespace Cosmos.Kernel.System.Filesystems.Fat;

internal sealed class FatOpenFile : IVfsOpenFile
{
    public FatOpenFile(FatInode inode, IFileOperations operations)
    {
        Inode = inode;
        Operations = operations;
    }

    public IVfsInode Inode { get; }

    public IFileOperations Operations { get; }

    public long Position { get; set; }
}
