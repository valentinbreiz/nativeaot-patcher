// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Services.VFS.Interfaces;

namespace Cosmos.Kernel.Services.VFS.FAT;

public class FatIsFileSystem : IIsFileSystem
{
    public bool IsFormat(Partition partition)
    {
        if (partition == null)
        {
            throw new ArgumentNullException(nameof(partition));
        }

        Span<byte> xBPB = partition.BlockDevice.NewBlockArray(1);
        partition.BlockDevice.ReadBlock(0UL, 1U, xBPB);

        ushort xSig = BitConverter.ToUInt16(xBPB.ToArray(), 510);
        return xSig == 0xAA55;
    }

    public IFileSystem GetFileSystem(Partition partition, string root)
    {
        return new FatFileSystem(partition, root);
    }
}
