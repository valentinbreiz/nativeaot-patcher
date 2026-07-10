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
    /// <summary>LBA of the boot sector holding the BPB — always the first sector of the volume (fatgen103 section 3.1). Public so the formatter, mount probe, and shell tooling read/write the same sector.</summary>
    public const ulong BootSectorLba = 0;

    // Boot-sector signature.

    /// <summary>Boot signature value at bytes 510-511.</summary>
    internal const ushort BootSignature = 0xAA55;

    /// <summary>Byte offset of the 0xAA55 boot signature.</summary>
    internal const int BootSignatureOffset = 510;

    // Boot sector / BPB layout (fatgen103 §3.1), ascending byte offset.

    /// <summary>BS_jmpBoot: 3-byte x86 jump opening the boot sector (offset 0).</summary>
    internal const int JmpBootOffset = 0;

    /// <summary>Byte offset of the jump displacement inside the 3-byte BS_jmpBoot field.</summary>
    internal const int JmpBootDisplacementOffset = JmpBootOffset + 1;

    /// <summary>Byte offset of the padding NOP inside the 3-byte BS_jmpBoot field.</summary>
    internal const int JmpBootNopOffset = JmpBootOffset + 2;

    /// <summary>BS_OEMName: OEM name string (offset 3, 8 bytes).</summary>
    internal const int OemNameOffset = 3;

    /// <summary>Byte length of the BS_OEMName field.</summary>
    internal const int OemNameLength = 8;

    /// <summary>BPB_BytsPerSec: bytes per sector (offset 11, 16-bit).</summary>
    internal const int BytsPerSecOffset = 11;

    /// <summary>BPB_SecPerClus: sectors per cluster (offset 13, 8-bit).</summary>
    internal const int SecPerClusOffset = 13;

    /// <summary>BPB_RsvdSecCnt: reserved sector count (offset 14, 16-bit).</summary>
    internal const int RsvdSecCntOffset = 14;

    /// <summary>BPB_NumFATs: FAT copy count (offset 16, 8-bit).</summary>
    internal const int NumFatsOffset = 16;

    /// <summary>BPB_RootEntCnt: root entry count (offset 17, 16-bit).</summary>
    internal const int RootEntCntOffset = 17;

    /// <summary>BPB_TotSec16: 16-bit total sector count (offset 19).</summary>
    internal const int TotSec16Offset = 19;

    /// <summary>BPB_Media: media descriptor (offset 21, 8-bit).</summary>
    internal const int MediaOffset = 21;

    /// <summary>BPB_FATSz16: 16-bit FAT size (offset 22).</summary>
    internal const int FatSz16Offset = 22;

    /// <summary>BPB_SecPerTrk: legacy CHS sectors per track (offset 24, 16-bit).</summary>
    internal const int SecPerTrkOffset = 24;

    /// <summary>BPB_NumHeads: legacy CHS head count (offset 26, 16-bit).</summary>
    internal const int NumHeadsOffset = 26;

    /// <summary>BPB_HiddSec: sectors hidden before the volume (offset 28, 32-bit).</summary>
    internal const int HiddSecOffset = 28;

    /// <summary>BPB_TotSec32: 32-bit total sector count (offset 32).</summary>
    internal const int TotSec32Offset = 32;

    // FAT12/16 extended boot record (offsets 36-61, fatgen103 §3.1).

    /// <summary>BS_DrvNum: BIOS drive number (offset 36 in the FAT12/16 layout).</summary>
    internal const int Fat1216DrvNumOffset = 36;

    /// <summary>BS_BootSig: extended boot signature (offset 38 in the FAT12/16 layout).</summary>
    internal const int Fat1216BootSigOffset = 38;

    /// <summary>BS_VolID: volume serial number (offset 39, 32-bit, FAT12/16 layout).</summary>
    internal const int Fat1216VolIdOffset = 39;

    /// <summary>BS_VolLab: volume label (offset 43, 11 bytes, FAT12/16 layout).</summary>
    internal const int Fat1216VolLabOffset = 43;

    /// <summary>BS_FilSysType: informational FS-type string (offset 54, 8 bytes, FAT12/16 layout).</summary>
    internal const int Fat1216FilSysTypeOffset = 54;

    // FAT32 BPB extension and extended boot record (offsets 36-89, fatgen103 §3.1).

    /// <summary>BPB_FATSz32: 32-bit FAT size (offset 36, FAT32 only).</summary>
    internal const int FatSz32Offset = 36;

    /// <summary>BPB_ExtFlags: FAT mirroring flags (offset 40, FAT32 only).</summary>
    internal const int ExtFlagsOffset = 40;

    /// <summary>BPB_FSVer: filesystem version (offset 42, FAT32 only).</summary>
    internal const int FsVerOffset = 42;

    /// <summary>BPB_RootClus: root directory cluster (offset 44, FAT32 only).</summary>
    internal const int RootClusOffset = 44;

    /// <summary>BPB_FSInfo: FSInfo sector number (offset 48, FAT32 only).</summary>
    internal const int FsInfoOffset = 48;

    /// <summary>BPB_BkBootSec: backup boot sector number (offset 50, FAT32 only).</summary>
    internal const int BkBootSecOffset = 50;

    /// <summary>BS_DrvNum: BIOS drive number (offset 64 in the FAT32 layout).</summary>
    internal const int Fat32DrvNumOffset = 64;

    /// <summary>BS_BootSig: extended boot signature (offset 66 in the FAT32 layout).</summary>
    internal const int Fat32BootSigOffset = 66;

    /// <summary>BS_VolID: volume serial number (offset 67, 32-bit, FAT32 layout).</summary>
    internal const int Fat32VolIdOffset = 67;

    /// <summary>BS_VolLab: volume label (offset 71, 11 bytes, FAT32 layout).</summary>
    internal const int Fat32VolLabOffset = 71;

    /// <summary>BS_FilSysType: informational FS-type string (offset 82, 8 bytes, FAT32 layout).</summary>
    internal const int Fat32FilSysTypeOffset = 82;

    // Field widths.

    /// <summary>Width in bytes of a 16-bit BPB field.</summary>
    internal const int UInt16FieldSize = 2;

    /// <summary>Width in bytes of a 32-bit BPB field.</summary>
    internal const int UInt32FieldSize = 4;

    /// <summary>Byte length of the BS_VolLab field: labels are stored space-padded to 11 chars.</summary>
    internal const int VolumeLabelLength = 11;

    /// <summary>Byte length of the BS_FilSysType field.</summary>
    internal const int FilSysTypeLength = 8;

    // Spec-legal geometry bounds.

    /// <summary>Smallest sector size the FAT spec permits (and the BPB layout floor).</summary>
    internal const uint MinBytesPerSector = 512;

    /// <summary>Largest sector size the FAT spec permits.</summary>
    internal const uint MaxBytesPerSector = 4096;

    /// <summary>Largest legal BPB_SecPerClus value: a power of two up to 128 (fatgen103 §3.1).</summary>
    private const uint MaxSectorsPerCluster = 128;

    // Cluster-count type bands (fatgen103 §3.5).

    /// <summary>Cluster counts at or above this are FAT16 (fatgen103 §3.5).</summary>
    private const uint Fat16MinClusters = 4085;

    /// <summary>Cluster counts at or above this are FAT32 (fatgen103 §3.5).</summary>
    private const uint Fat32MinClusters = 65525;

    /// <summary>Highest cluster count of the FAT12 band — the Max encoding of the same §3.5 boundary as <see cref="Fat16MinClusters"/> (Max = Min - 1).</summary>
    internal const uint Fat12MaxClusters = Fat16MinClusters - 1;

    /// <summary>Highest cluster count of the FAT16 band — the Max encoding of the same §3.5 boundary as <see cref="Fat32MinClusters"/> (Max = Min - 1).</summary>
    internal const uint Fat16MaxClusters = Fat32MinClusters - 1;

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

        if (bpb.Length < (int)MinBytesPerSector)
        {
            return false;
        }

        if (BitConverter.ToUInt16(bpb.Slice(BootSignatureOffset, UInt16FieldSize)) != BootSignature)
        {
            return false;
        }

        FatBootSector bs = new()
        {
            BytesPerSector = BitConverter.ToUInt16(bpb.Slice(BytsPerSecOffset, UInt16FieldSize)),
            SectorsPerCluster = bpb[SecPerClusOffset],
            ReservedSectorCount = BitConverter.ToUInt16(bpb.Slice(RsvdSecCntOffset, UInt16FieldSize)),
            NumberOfFats = bpb[NumFatsOffset],
            RootEntryCount = BitConverter.ToUInt16(bpb.Slice(RootEntCntOffset, UInt16FieldSize)),
        };

        // On-disk BPB fields are untrusted (same rule as Mbr/Gpt/Ebr):
        // enforce the spec-legal ranges the derived math and FatTable's
        // entry-spanning logic assume. BytesPerSector must be one of
        // 512/1024/2048/4096, SectorsPerCluster a power of two <= 128.
        if (bs.BytesPerSector < MinBytesPerSector || bs.BytesPerSector > MaxBytesPerSector
            || (bs.BytesPerSector & (bs.BytesPerSector - 1)) != 0
            || bs.SectorsPerCluster == 0 || bs.SectorsPerCluster > MaxSectorsPerCluster
            || (bs.SectorsPerCluster & (bs.SectorsPerCluster - 1)) != 0
            || bs.NumberOfFats == 0)
        {
            return false;
        }

        bs.BytesPerCluster = bs.BytesPerSector * bs.SectorsPerCluster;

        uint total16 = BitConverter.ToUInt16(bpb.Slice(TotSec16Offset, UInt16FieldSize));
        bs.TotalSectorCount = total16 != 0 ? total16 : BitConverter.ToUInt32(bpb.Slice(TotSec32Offset, UInt32FieldSize));

        uint fat16 = BitConverter.ToUInt16(bpb.Slice(FatSz16Offset, UInt16FieldSize));
        bs.FatSectorCount = fat16 != 0 ? fat16 : BitConverter.ToUInt32(bpb.Slice(FatSz32Offset, UInt32FieldSize));

        if (bs.TotalSectorCount == 0 || bs.FatSectorCount == 0)
        {
            return false;
        }

        bs.FatStartLba = bs.ReservedSectorCount;
        bs.RootSectorCount = (bs.RootEntryCount * (uint)FatDirectory.EntrySize + (bs.BytesPerSector - 1)) / bs.BytesPerSector;

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

        if (bs.ClusterCount < Fat16MinClusters)
        {
            bs.Type = FatType.Fat12;
        }
        else if (bs.ClusterCount < Fat32MinClusters)
        {
            bs.Type = FatType.Fat16;
        }
        else
        {
            bs.Type = FatType.Fat32;
        }

        if (bs.Type == FatType.Fat32)
        {
            bs.RootCluster = BitConverter.ToUInt32(bpb.Slice(RootClusOffset, UInt32FieldSize));
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
        if (cluster < FatTable.FirstDataCluster || cluster > ClusterCount + 1)
        {
            throw new ArgumentOutOfRangeException(nameof(cluster));
        }
        return DataStartLba + ((ulong)(cluster - FatTable.FirstDataCluster)) * SectorsPerCluster;
    }
}
