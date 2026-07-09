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

        // On-disk BPB fields are untrusted (same rule as Mbr/Gpt/Ebr):
        // enforce the spec-legal ranges the derived math and FatTable's
        // entry-spanning logic assume. BytesPerSector must be one of
        // 512/1024/2048/4096, SectorsPerCluster a power of two <= 128.
        if (bs.BytesPerSector < 512 || bs.BytesPerSector > 4096
            || (bs.BytesPerSector & (bs.BytesPerSector - 1)) != 0
            || bs.SectorsPerCluster == 0 || bs.SectorsPerCluster > 128
            || (bs.SectorsPerCluster & (bs.SectorsPerCluster - 1)) != 0
            || bs.NumberOfFats == 0)
        {
            return false;
        }

        bs.BytesPerCluster = bs.BytesPerSector * bs.SectorsPerCluster;

        uint total16 = BitConverter.ToUInt16(bpb.Slice(19, 2));
        bs.TotalSectorCount = total16 != 0 ? total16 : BitConverter.ToUInt32(bpb.Slice(32, 4));

        uint fat16 = BitConverter.ToUInt16(bpb.Slice(22, 2));
        bs.FatSectorCount = fat16 != 0 ? fat16 : BitConverter.ToUInt32(bpb.Slice(36, 4));

        if (bs.TotalSectorCount == 0 || bs.FatSectorCount == 0)
        {
            return false;
        }

        bs.FatStartLba = bs.ReservedSectorCount;
        bs.RootSectorCount = (bs.RootEntryCount * 32u + (bs.BytesPerSector - 1)) / bs.BytesPerSector;

        // Derive the geometry in ulong: a crafted FATSz32/NumFATs pair
        // (e.g. 0x80000000 x 2) wraps the uint product to 0 and collapses
        // the data area onto the reserved sectors.
        ulong fatRegion = (ulong)bs.NumberOfFats * bs.FatSectorCount;
        ulong rootStart = bs.ReservedSectorCount + fatRegion;
        ulong dataStart = rootStart + bs.RootSectorCount;
        if (dataStart >= bs.TotalSectorCount)
        {
            return false;
        }
        bs.RootStartLba = (uint)rootStart;
        bs.DataStartLba = (uint)dataStart;

        uint dataSectorCount = bs.TotalSectorCount - bs.DataStartLba;
        bs.ClusterCount = dataSectorCount / bs.SectorsPerCluster;
        if (bs.ClusterCount == 0)
        {
            return false;
        }

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
            bs.DataStartLba = (uint)rootStart;
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
        // Data clusters are 2..ClusterCount+1. Below 2 the uint
        // subtraction underflows (cluster 0 yields an exabyte-range LBA),
        // above addresses past the volume — callers get a throw instead
        // of wild device I/O.
        if (cluster < 2 || cluster > ClusterCount + 1)
        {
            throw new ArgumentOutOfRangeException(nameof(cluster));
        }
        return DataStartLba + ((ulong)(cluster - 2)) * SectorsPerCluster;
    }
}
