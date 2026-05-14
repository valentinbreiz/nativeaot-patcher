using Cosmos.Kernel.HAL.Vfs;
using Cosmos.Kernel.System.Filesystems.Fat;

namespace Cosmos.Kernel.Tests.Fat;

/// <summary>
/// Builds clean FAT16 / FAT32 volumes on a <see cref="MemoryBlockDevice"/> by
/// calling <see cref="FatFilesystemType.TryFormat(System.ReadOnlySpan{char}, IVfsFormatOptions?)"/>
/// — the same path real kernel code would use to mkfs a partition. The
/// volume sizes and explicit overrides are chosen so the resulting cluster
/// counts land squarely in the FAT family the test asks for.
/// </summary>
internal static class FatTestVolume
{
    public const ulong BlockSize = 512;

    // FAT16 geometry: 32 MiB / SPC=8 / 32-sector FAT keeps cluster count in the
    // [4085, 65525) band even with the formatter's auto-pick branches.
    private const ulong Fat16DiskSizeBytes = 32UL * 1024 * 1024;
    private const ulong Fat16BlockCount = Fat16DiskSizeBytes / BlockSize;

    // FAT32 geometry: 33 MiB / SPC=1 / 512-sector FAT yields cluster count > 65525.
    private const ulong Fat32DiskSizeBytes = 33UL * 1024 * 1024;
    private const ulong Fat32BlockCount = Fat32DiskSizeBytes / BlockSize;

    public static MemoryBlockDevice CreateFat16(string name)
    {
        MemoryBlockDevice device = new(name, BlockSize, Fat16BlockCount);
        FatFilesystemType driver = new(device);
        FatFormatOptions options = new()
        {
            Type = FatType.Fat16,
            SectorsPerCluster = 8,
            ReservedSectorCount = 1,
            NumberOfFats = 2,
            RootEntryCount = 512,
            FatSectorCount = 32,
            VolumeLabel = "COSMOSFAT  ",
        };
        if (!driver.TryFormat(default, options))
        {
            throw new InvalidOperationException("FAT16 format failed");
        }
        return device;
    }

    public static MemoryBlockDevice CreateFat32(string name)
    {
        MemoryBlockDevice device = new(name, BlockSize, Fat32BlockCount);
        FatFilesystemType driver = new(device);
        FatFormatOptions options = new()
        {
            Type = FatType.Fat32,
            SectorsPerCluster = 1,
            ReservedSectorCount = 32,
            NumberOfFats = 2,
            FatSectorCount = 512,
            RootCluster = 2,
            VolumeLabel = "COSMOSFAT32",
        };
        if (!driver.TryFormat(default, options))
        {
            throw new InvalidOperationException("FAT32 format failed");
        }
        return device;
    }
}
