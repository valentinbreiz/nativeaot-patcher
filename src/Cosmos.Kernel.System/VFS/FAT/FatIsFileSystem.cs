// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.System.VFS.Interfaces;

namespace Cosmos.Kernel.System.VFS.FAT;

public class FatIsFileSystem : IIsFileSystem
{
    public bool IsFormat(Partition partition)
    {
        if (partition == null)
        {
            throw new ArgumentNullException(nameof(partition));
        }

        Span<byte> xBPB = partition.NewBlockArray(1);
        partition.ReadBlock(0UL, 1U, xBPB);

        ushort xSig = BitConverter.ToUInt16(xBPB.Slice(510, 2));
        return xSig == 0xAA55;
    }

    public IFileSystem GetFileSystem(Partition partition, string root)
    {
        return new FatFileSystem(partition, root);
    }
}
