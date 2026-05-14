// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.HAL.Interfaces.Devices;

namespace Cosmos.Kernel.System.Filesystems.Fat;

/// <summary>
/// FAT family identifier; selected from cluster count per the FAT32 spec.
/// </summary>
public enum FatType
{
    Unknown,
    Fat12,
    Fat16,
    Fat32,
}

/// <summary>
/// Parsed BIOS Parameter Block plus derived geometry for a FAT volume.
/// All <c>*Lba</c> fields are absolute on the underlying
/// <see cref="IBlockDevice"/> the BPB was read from.
/// </summary>
public sealed class FatBootSector
{
    private const ushort BootSignature = 0xAA55;

    public FatType Type { get; private set; }
    public uint BytesPerSector { get; private set; }
    public uint SectorsPerCluster { get; private set; }
    public uint BytesPerCluster { get; private set; }
    public uint ReservedSectorCount { get; private set; }
    public uint NumberOfFats { get; private set; }
    public uint RootEntryCount { get; private set; }
    public uint TotalSectorCount { get; private set; }
    public uint FatSectorCount { get; private set; }
    public uint RootCluster { get; private set; }
    public uint RootStartLba { get; private set; }
    public uint RootSectorCount { get; private set; }
    public uint DataStartLba { get; private set; }
    public uint ClusterCount { get; private set; }
    public uint FatStartLba { get; private set; }

    private FatBootSector() { }

    public static bool TryParse(ReadOnlySpan<byte> bpb, out FatBootSector? bootSector)
    {
        bootSector = null;

        if (bpb.Length < 512)
        {
            return false;
        }

        if (BitConverter.ToUInt16(bpb.Slice(510, 2)) != BootSignature)
        {
            return false;
        }

        FatBootSector bs = new()
        {
            BytesPerSector = BitConverter.ToUInt16(bpb.Slice(11, 2)),
            SectorsPerCluster = bpb[13],
            ReservedSectorCount = BitConverter.ToUInt16(bpb.Slice(14, 2)),
            NumberOfFats = bpb[16],
            RootEntryCount = BitConverter.ToUInt16(bpb.Slice(17, 2)),
        };

        if (bs.BytesPerSector == 0 || bs.SectorsPerCluster == 0 || bs.NumberOfFats == 0)
        {
            return false;
        }

        bs.BytesPerCluster = bs.BytesPerSector * bs.SectorsPerCluster;

        uint total16 = BitConverter.ToUInt16(bpb.Slice(19, 2));
        bs.TotalSectorCount = total16 != 0 ? total16 : BitConverter.ToUInt32(bpb.Slice(32, 4));

        uint fat16 = BitConverter.ToUInt16(bpb.Slice(22, 2));
        bs.FatSectorCount = fat16 != 0 ? fat16 : BitConverter.ToUInt32(bpb.Slice(36, 4));

        bs.FatStartLba = bs.ReservedSectorCount;
        bs.RootSectorCount = (bs.RootEntryCount * 32u + (bs.BytesPerSector - 1)) / bs.BytesPerSector;

        uint fatRegion = bs.NumberOfFats * bs.FatSectorCount;
        bs.RootStartLba = bs.ReservedSectorCount + fatRegion;
        bs.DataStartLba = bs.RootStartLba + bs.RootSectorCount;

        uint dataSectorCount = bs.TotalSectorCount > bs.DataStartLba
            ? bs.TotalSectorCount - bs.DataStartLba
            : 0;
        bs.ClusterCount = dataSectorCount / bs.SectorsPerCluster;

        if (bs.ClusterCount < 4085)
        {
            bs.Type = FatType.Fat12;
        }
        else if (bs.ClusterCount < 65525)
        {
            bs.Type = FatType.Fat16;
        }
        else
        {
            bs.Type = FatType.Fat32;
        }

        if (bs.Type == FatType.Fat32)
        {
            bs.RootCluster = BitConverter.ToUInt32(bpb.Slice(44, 4));
            bs.RootStartLba = 0;
            bs.RootSectorCount = 0;
            bs.DataStartLba = bs.ReservedSectorCount + fatRegion;
        }
        else
        {
            bs.RootCluster = 0;
        }

        bootSector = bs;
        return true;
    }

    /// <summary>Absolute LBA of the first sector of <paramref name="cluster"/>.</summary>
    public ulong ClusterToLba(uint cluster)
    {
        return DataStartLba + ((ulong)(cluster - 2)) * SectorsPerCluster;
    }
}
