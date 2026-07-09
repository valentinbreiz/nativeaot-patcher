// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.HAL.Vfs;

namespace Cosmos.Kernel.System.Filesystems.Fat;

internal sealed class FatSuperblockOperations : ISuperblockOperations
{
    public const ulong Magic = 0x4D7341544146u;

    public bool Sync(IVfsSuperblock superblock)
    {
        if (superblock is not FatSuperblock fat)
        {
            return false;
        }
        // Writes are durable only after Flush per the IBlockDevice
        // contract; sync is exactly that durability point.
        fat.Flush();
        return true;
    }

    public bool StatFs(IVfsSuperblock superblock, out VfsStatFs statFs)
    {
        statFs = default;
        if (superblock is not FatSuperblock fat)
        {
            return false;
        }

        FatBootSector boot = fat.Boot;
        statFs.Type = Magic;
        statFs.BlockSize = boot.BytesPerCluster;
        statFs.Blocks = boot.ClusterCount;
        statFs.Bfree = fat.Fat.CountFree();
        statFs.Bavail = statFs.Bfree;
        statFs.Files = 0;
        statFs.Ffree = 0;
        statFs.NameMax = fat.MaxNameLength;
        statFs.Frsize = boot.BytesPerSector;
        return true;
    }

    public void Drop(IVfsSuperblock superblock)
    {
        if (superblock is FatSuperblock fat)
        {
            // Unmount is a durability point: flush before tearing down.
            fat.Flush();
            fat.Drop();
        }
    }
}
