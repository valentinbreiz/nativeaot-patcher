// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.HAL.Interfaces.Devices;

namespace Cosmos.Kernel.System.Filesystems.Fat;

/// <summary>
/// Lays down a fresh FAT12 / FAT16 / FAT32 volume on an
/// <see cref="IBlockDevice"/>: writes the BPB, all FAT copies (with reserved
/// entries plus the root-cluster EOC marker on FAT32), and zeroes the root
/// directory area. Pure I/O against the device — no superblock or VFS state.
/// </summary>
internal static class FatFormatter
{
    /// <summary>BS_jmpBoot: 3-byte x86 jump opening the boot sector (offset 0, fatgen103 §3.1).</summary>
    private const int JmpBootOffset = 0;

    /// <summary>Byte offset of the jump displacement inside the 3-byte BS_jmpBoot field.</summary>
    private const int JmpBootDisplacementOffset = JmpBootOffset + 1;

    /// <summary>Byte offset of the padding NOP inside the 3-byte BS_jmpBoot field.</summary>
    private const int JmpBootNopOffset = JmpBootOffset + 2;

    /// <summary>BS_OEMName: OEM name string (offset 3, 8 bytes).</summary>
    private const int OemNameOffset = 3;

    /// <summary>Byte length of the BS_OEMName field.</summary>
    private const int OemNameLength = 8;

    /// <summary>BPB_Media: media descriptor (offset 21, 8-bit).</summary>
    private const int MediaOffset = 21;

    /// <summary>BPB_SecPerTrk: legacy CHS sectors per track (offset 24, 16-bit).</summary>
    private const int SecPerTrkOffset = 24;

    /// <summary>BPB_NumHeads: legacy CHS head count (offset 26, 16-bit).</summary>
    private const int NumHeadsOffset = 26;

    /// <summary>BPB_HiddSec: sectors hidden before the volume (offset 28, 32-bit).</summary>
    private const int HiddSecOffset = 28;

    /// <summary>BPB_ExtFlags: FAT mirroring flags (offset 40, FAT32 only).</summary>
    private const int ExtFlagsOffset = 40;

    /// <summary>BPB_FSVer: filesystem version (offset 42, FAT32 only).</summary>
    private const int FsVerOffset = 42;

    /// <summary>BPB_FSInfo: FSInfo sector number (offset 48, FAT32 only).</summary>
    private const int FsInfoOffset = 48;

    /// <summary>BPB_BkBootSec: backup boot sector number (offset 50, FAT32 only).</summary>
    private const int BkBootSecOffset = 50;

    /// <summary>BS_DrvNum: BIOS drive number (offset 64 in the FAT32 layout).</summary>
    private const int Fat32DrvNumOffset = 64;

    /// <summary>BS_BootSig: extended boot signature (offset 66 in the FAT32 layout).</summary>
    private const int Fat32BootSigOffset = 66;

    /// <summary>BS_VolID: volume serial number (offset 67, 32-bit, FAT32 layout).</summary>
    private const int Fat32VolIdOffset = 67;

    /// <summary>BS_VolLab: volume label (offset 71, 11 bytes, FAT32 layout).</summary>
    private const int Fat32VolLabOffset = 71;

    /// <summary>BS_FilSysType: informational FS-type string (offset 82, 8 bytes, FAT32 layout).</summary>
    private const int Fat32FilSysTypeOffset = 82;

    /// <summary>BS_DrvNum: BIOS drive number (offset 36 in the FAT12/16 layout).</summary>
    private const int Fat1216DrvNumOffset = 36;

    /// <summary>BS_BootSig: extended boot signature (offset 38 in the FAT12/16 layout).</summary>
    private const int Fat1216BootSigOffset = 38;

    /// <summary>BS_VolID: volume serial number (offset 39, 32-bit, FAT12/16 layout).</summary>
    private const int Fat1216VolIdOffset = 39;

    /// <summary>BS_VolLab: volume label (offset 43, 11 bytes, FAT12/16 layout).</summary>
    private const int Fat1216VolLabOffset = 43;

    /// <summary>BS_FilSysType: informational FS-type string (offset 54, 8 bytes, FAT12/16 layout).</summary>
    private const int Fat1216FilSysTypeOffset = 54;

    /// <summary>FSI_LeadSig: FSInfo lead signature (offset 0, fatgen103 §5).</summary>
    private const int FsiLeadSigOffset = 0;

    /// <summary>FSI_StrucSig: FSInfo structure signature (offset 484, fatgen103 §5).</summary>
    private const int FsiStrucSigOffset = 484;

    /// <summary>FSI_Free_Count: last-known free cluster count (offset 488, fatgen103 §5).</summary>
    private const int FsiFreeCountOffset = 488;

    /// <summary>FSI_Nxt_Free: free-cluster search hint (offset 492, fatgen103 §5).</summary>
    private const int FsiNextFreeOffset = 492;

    /// <summary>FSI_TrailSig: FSInfo trail signature (offset 508, fatgen103 §5).</summary>
    private const int FsiTrailSigOffset = 508;

    /// <summary>Byte length of the BS_VolLab field: labels are stored space-padded to 11 chars.</summary>
    private const int VolumeLabelLength = 11;

    /// <summary>Byte length of the BS_FilSysType field.</summary>
    private const int FilSysTypeLength = 8;

    /// <summary>Highest cluster count of the FAT12 band (fatgen103 §3.5).</summary>
    private const uint Fat12MaxClusters = 4084;

    /// <summary>Highest cluster count of the FAT16 band (fatgen103 §3.5).</summary>
    private const uint Fat16MaxClusters = 65524;

    /// <summary>Smallest device (in sectors) <see cref="Format"/> will attempt; anything smaller cannot hold even the reserved head.</summary>
    private const ulong MinDeviceSectors = 8;

    /// <summary>Default BPB_RsvdSecCnt for FAT32 volumes (fatgen103 §3.1 typical value 32).</summary>
    private const ushort Fat32DefaultReservedSectors = 32;

    /// <summary>Default BPB_RsvdSecCnt for FAT12/16 volumes (fatgen103 §3.1: should never be other than 1).</summary>
    private const ushort Fat1216DefaultReservedSectors = 1;

    /// <summary>Default BPB_NumFATs: two mirrored FAT copies (fatgen103 §3.1).</summary>
    internal const byte DefaultNumberOfFats = 2;

    /// <summary>Default BPB_RootEntCnt for FAT12/16: 512 root directory entries (fatgen103 §3.1).</summary>
    private const ushort DefaultRootEntryCount = 512;

    /// <summary>Default sectors per cluster for FAT12/16 volumes (4 KiB clusters at 512-byte sectors).</summary>
    private const byte Fat1216DefaultSectorsPerCluster = 8;

    /// <summary>fatgen103 §3.4 FAT32 table: volumes up to this many sectors (~260 MiB) use <see cref="Fat32SpcUpTo260MiB"/>.</summary>
    private const uint Fat32SectorLimit260MiB = 532_480;

    /// <summary>fatgen103 §3.4 FAT32 table: volumes up to this many sectors (~8 GiB) use <see cref="Fat32SpcUpTo8GiB"/>.</summary>
    private const uint Fat32SectorLimit8GiB = 16_777_216;

    /// <summary>fatgen103 §3.4 FAT32 table: volumes up to this many sectors (~16 GiB) use <see cref="Fat32SpcUpTo16GiB"/>.</summary>
    private const uint Fat32SectorLimit16GiB = 33_554_432;

    /// <summary>fatgen103 §3.4 FAT32 table: volumes up to this many sectors (~32 GiB) use <see cref="Fat32SpcUpTo32GiB"/>.</summary>
    private const uint Fat32SectorLimit32GiB = 67_108_864;

    /// <summary>FAT32 sectors per cluster below the ~260 MiB threshold (0.5 KiB clusters, fatgen103 §3.4).</summary>
    private const byte Fat32SpcUpTo260MiB = 1;

    /// <summary>FAT32 sectors per cluster below the ~8 GiB threshold (4 KiB clusters, fatgen103 §3.4).</summary>
    private const byte Fat32SpcUpTo8GiB = 8;

    /// <summary>FAT32 sectors per cluster below the ~16 GiB threshold (8 KiB clusters, fatgen103 §3.4).</summary>
    private const byte Fat32SpcUpTo16GiB = 16;

    /// <summary>FAT32 sectors per cluster below the ~32 GiB threshold (16 KiB clusters, fatgen103 §3.4).</summary>
    private const byte Fat32SpcUpTo32GiB = 32;

    /// <summary>FAT32 sectors per cluster at or above the ~32 GiB threshold (32 KiB clusters, fatgen103 §3.4).</summary>
    private const byte Fat32SpcAbove32GiB = 64;

    /// <summary>Sector number of the FAT32 FSInfo block, mirrored into the BPB at offset 48.</summary>
    private const ushort FsInfoSectorNumber = 1;

    /// <summary>Sector number of the FAT32 backup boot sector, mirrored into the BPB at offset 50.</summary>
    private const ushort BackupBootSectorNumber = 6;

    /// <summary>Sectors wiped by <see cref="Destroy"/>: covers the boot sector, FSInfo and the backup boot sector.</summary>
    private const ulong DestroyWipeSectors = 8;

    /// <summary>Media descriptor for fixed disks.</summary>
    private const byte MediaDescriptorFixed = 0xF8;

    /// <summary>BIOS drive number for the first fixed disk.</summary>
    private const byte DriveNumberFixedDisk = 0x80;

    /// <summary>Extended boot signature marking the serial/label/type fields as present.</summary>
    private const byte ExtendedBootSignature = 0x29;

    /// <summary>Deterministic default volume serial (no RTC entropy in the boot path).</summary>
    private const uint DefaultVolumeSerial = 0xC051D2A3;

    /// <summary>x86 short-jump opcode opening BS_jmpBoot.</summary>
    private const byte JmpBootShortJumpOpcode = 0xEB;

    /// <summary>BS_jmpBoot displacement for the FAT32 layout: jumps over the 90-byte BPB/header to offset 0x5A.</summary>
    private const byte Fat32JmpBootDisplacement = 0x58;

    /// <summary>BS_jmpBoot displacement for the FAT12/16 layout: jumps over the 62-byte BPB/header to offset 0x3E.</summary>
    private const byte Fat1216JmpBootDisplacement = 0x3C;

    /// <summary>x86 NOP padding the third BS_jmpBoot byte.</summary>
    private const byte JmpBootNopOpcode = 0x90;

    /// <summary>BPB_SecPerTrk value stamped for legacy CHS tools (unused by LBA addressing).</summary>
    private const ushort LegacySectorsPerTrack = 63;

    /// <summary>BPB_NumHeads value stamped for legacy CHS tools (unused by LBA addressing).</summary>
    private const ushort LegacyHeadCount = 16;

    /// <summary>Fill byte of the packed FAT12 reserved entries 0-1 past the media descriptor (all ones, fatgen103 §4).</summary>
    private const byte Fat12ReservedFill = 0xFF;

    /// <summary>FAT16 FAT[0]: media descriptor in the low byte, all remaining bits ones (fatgen103 §4).</summary>
    private const ushort Fat16Fat0Entry = 0xFFF8;

    /// <summary>FAT16 FAT[1]: end-of-chain value with the clean-shutdown/error flag bits set (fatgen103 §4).</summary>
    private const ushort Fat16Fat1Entry = 0xFFFF;

    /// <summary>FAT32 FAT[0]: media descriptor in the low byte, remaining 28-bit entry all ones (fatgen103 §4).</summary>
    private const uint Fat32Fat0Entry = 0x0FFFFFF8u;

    /// <summary>FAT32 end-of-chain value written to FAT[1] and the root cluster's FAT entry (fatgen103 §4).</summary>
    private const uint Fat32EndOfChainEntry = 0x0FFFFFFFu;

    /// <summary>FSI_LeadSig value "RRaA" (fatgen103 §5).</summary>
    private const uint FsiLeadSig = 0x41615252u;

    /// <summary>FSI_StrucSig value "rrAa" (fatgen103 §5).</summary>
    private const uint FsiStrucSig = 0x61417272u;

    /// <summary>FSI_TrailSig value closing the FSInfo sector (fatgen103 §5).</summary>
    private const uint FsiTrailSig = 0xAA550000u;

    /// <summary>FSI_Free_Count / FSI_Nxt_Free "unknown" value (fatgen103 §5).</summary>
    private const uint FsiUnknownValue = 0xFFFFFFFFu;

    /// <summary>Sectors zeroed per WriteBlock batch when clearing FAT / root areas.</summary>
    private const uint ZeroBatchSectors = 64;

    public static bool Format(IBlockDevice device, FatFormatOptions? options)
    {
        if (device == null || device.BlockCount < MinDeviceSectors)
        {
            return false;
        }

        FatFormatOptions opts = options ?? new FatFormatOptions();

        if (!ResolveGeometry(device, opts, out FormatGeometry geom))
        {
            return false;
        }

        WriteBootSector(device, geom);
        if (geom.Type == FatType.Fat32)
        {
            WriteFat32FsInfo(device, geom);
            WriteFat32BackupBoot(device, geom);
        }
        InitializeFats(device, geom);
        ZeroRootArea(device, geom);

        // Per the IBlockDevice contract, durability across power loss is
        // only guaranteed after Flush — and mkfs is exactly where the
        // caller assumes the on-disk state is real once it sees true.
        device.Flush();
        return true;
    }

    public static bool Destroy(IBlockDevice device)
    {
        if (device == null || device.BlockCount < 1)
        {
            return false;
        }

        // Wipe the whole label head, not just LBA 0: FAT32 volumes also
        // carry an FSInfo sector and a byte-identical backup boot sector
        // that BPB_BkBootSec-honoring tools can reconstruct a mount from.
        ulong wipeSectors = DestroyWipeSectors < device.BlockCount ? DestroyWipeSectors : device.BlockCount;
        Span<byte> zero = new byte[(int)device.BlockSize];
        for (ulong i = 0; i < wipeSectors; i++)
        {
            device.WriteBlock(i, 1, zero);
        }
        device.Flush();
        return true;
    }

    private readonly struct FormatGeometry
    {
        public FormatGeometry(
            FatType type,
            uint bytesPerSector,
            byte sectorsPerCluster,
            ushort reservedSectorCount,
            byte numberOfFats,
            ushort rootEntryCount,
            uint fatSectorCount,
            uint totalSectorCount,
            uint rootCluster,
            string label,
            uint serial)
        {
            Type = type;
            BytesPerSector = bytesPerSector;
            SectorsPerCluster = sectorsPerCluster;
            ReservedSectorCount = reservedSectorCount;
            NumberOfFats = numberOfFats;
            RootEntryCount = rootEntryCount;
            FatSectorCount = fatSectorCount;
            TotalSectorCount = totalSectorCount;
            RootCluster = rootCluster;
            Label = label;
            Serial = serial;
        }

        public FatType Type { get; }
        public uint BytesPerSector { get; }
        public byte SectorsPerCluster { get; }
        public ushort ReservedSectorCount { get; }
        public byte NumberOfFats { get; }
        public ushort RootEntryCount { get; }
        public uint FatSectorCount { get; }
        public uint TotalSectorCount { get; }
        public uint RootCluster { get; }
        public string Label { get; }
        public uint Serial { get; }

        public uint RootDirSectors => (uint)(RootEntryCount * (uint)FatDirectory.EntrySize + (BytesPerSector - 1)) / BytesPerSector;
        public uint FatRegion => NumberOfFats * FatSectorCount;
        public uint DataStart => Type == FatType.Fat32
            ? ReservedSectorCount + FatRegion
            : ReservedSectorCount + FatRegion + RootDirSectors;
    }

    private static bool ResolveGeometry(IBlockDevice device, FatFormatOptions opts, out FormatGeometry geom)
    {
        geom = default;

        uint bytesPerSector = (uint)device.BlockSize;
        // The FAT spec permits exactly 512/1024/2048/4096-byte sectors:
        // smaller crashes the fixed-offset BPB writer, larger truncates
        // the 16-bit BPB field into an unmountable volume. Writers reject
        // what the parser would drop.
        if (bytesPerSector < FatBootSector.MinBytesPerSector || bytesPerSector > FatBootSector.MaxBytesPerSector
            || (bytesPerSector & (bytesPerSector - 1)) != 0
            || device.BlockCount > uint.MaxValue)
        {
            return false;
        }

        uint totalSectors = (uint)device.BlockCount;
        FatType type = opts.Type;
        byte spc = opts.SectorsPerCluster != 0 ? opts.SectorsPerCluster : PickSectorsPerCluster(totalSectors, type);
        if (spc == 0 || (spc & (spc - 1)) != 0)
        {
            return false;
        }

        ushort reserved = opts.ReservedSectorCount != 0
            ? opts.ReservedSectorCount
            : (type == FatType.Fat32 ? Fat32DefaultReservedSectors : Fat1216DefaultReservedSectors);
        byte numFats = opts.NumberOfFats == 0 ? DefaultNumberOfFats : opts.NumberOfFats;
        ushort rootEntries = type == FatType.Fat32
            ? (ushort)0
            : (opts.RootEntryCount != 0 ? opts.RootEntryCount : DefaultRootEntryCount);
        uint rootCluster = type == FatType.Fat32
            ? (opts.RootCluster != 0 ? opts.RootCluster : FatTable.FirstDataCluster)
            : 0u;

        uint fatSectors = opts.FatSectorCount != 0
            ? opts.FatSectorCount
            : ComputeFatSectors(type, totalSectors, bytesPerSector, reserved, numFats, spc, rootEntries);
        if (fatSectors == 0)
        {
            return false;
        }

        // Decide / validate type from cluster count.
        uint rootDirSectors = (uint)(rootEntries * (uint)FatDirectory.EntrySize + (bytesPerSector - 1)) / bytesPerSector;
        uint fatRegion = (uint)numFats * fatSectors;
        uint dataStart = reserved + fatRegion + rootDirSectors;
        if (totalSectors <= dataStart)
        {
            return false;
        }
        uint clusterCount = (totalSectors - dataStart) / spc;

        FatType resolved = type;
        if (resolved == FatType.Unknown)
        {
            resolved = clusterCount <= Fat12MaxClusters ? FatType.Fat12
                : (clusterCount <= Fat16MaxClusters ? FatType.Fat16 : FatType.Fat32);
            // FAT32 needs a different layout (reserved=32, no root dir region).
            if (resolved == FatType.Fat32)
            {
                reserved = opts.ReservedSectorCount != 0 ? opts.ReservedSectorCount : Fat32DefaultReservedSectors;
                rootEntries = 0;
                rootCluster = opts.RootCluster != 0 ? opts.RootCluster : FatTable.FirstDataCluster;
                rootDirSectors = 0;
            }
            // The first fatSectors guess used the FAT12 fallback
            // denominator; a volume resolved as FAT16/FAT32 needs a
            // denser FAT (2/4-byte entries), so recompute with the
            // resolved type and re-derive the layout.
            fatSectors = opts.FatSectorCount != 0
                ? opts.FatSectorCount
                : ComputeFatSectors(resolved, totalSectors, bytesPerSector, reserved, numFats, spc, rootEntries);
            if (fatSectors == 0)
            {
                return false;
            }
            fatRegion = (uint)numFats * fatSectors;
            dataStart = reserved + fatRegion + rootDirSectors;
            if (totalSectors <= dataStart)
            {
                return false;
            }
            clusterCount = (totalSectors - dataStart) / spc;
            // The recomputed layout must not drift out of the resolved
            // band (possible right at a band boundary).
            if (!ValidateBand(resolved, clusterCount))
            {
                return false;
            }
        }
        else
        {
            if (!ValidateBand(resolved, clusterCount))
            {
                return false;
            }
        }

        // The FAT32 root cluster must be a real data cluster: below 2
        // underflows ZeroRootArea/ClusterToLba into wild LBAs, beyond the
        // cluster count the EOC write lands outside the FAT region.
        if (resolved == FatType.Fat32
            && (rootCluster < FatTable.FirstDataCluster || rootCluster >= clusterCount + FatTable.FirstDataCluster))
        {
            return false;
        }

        // The FAT12/16 BPB stores the FAT size in a 16-bit field: never
        // stamp a value that parses back differently than it was written.
        if (resolved != FatType.Fat32 && fatSectors > ushort.MaxValue)
        {
            return false;
        }

        string label = string.IsNullOrEmpty(opts.VolumeLabel) ? "NO NAME    " : PadLabel(opts.VolumeLabel!);
        uint serial = opts.VolumeSerial != 0 ? opts.VolumeSerial : DefaultVolumeSerial;

        geom = new FormatGeometry(
            resolved,
            bytesPerSector,
            spc,
            reserved,
            numFats,
            rootEntries,
            fatSectors,
            totalSectors,
            rootCluster,
            label,
            serial);
        return true;
    }

    private static byte PickSectorsPerCluster(uint totalSectors, FatType requested)
    {
        // FAT32 follows Microsoft fatgen103 §3.4: SPC must keep cluster count
        // above 65525 so we stay in the FAT32 band. The table below matches
        // what Windows/format.com pick.
        if (requested == FatType.Fat32)
        {
            // < 260 MiB → SPC 1 (0.5 KiB cluster). Anything smaller than
            // ~32 MiB still fits, just barely.
            if (totalSectors < Fat32SectorLimit260MiB)
            {
                return Fat32SpcUpTo260MiB;
            }
            // < 8 GiB → SPC 8 (4 KiB cluster).
            if (totalSectors < Fat32SectorLimit8GiB)
            {
                return Fat32SpcUpTo8GiB;
            }
            // < 16 GiB → SPC 16 (8 KiB).
            if (totalSectors < Fat32SectorLimit16GiB)
            {
                return Fat32SpcUpTo16GiB;
            }
            // < 32 GiB → SPC 32 (16 KiB).
            if (totalSectors < Fat32SectorLimit32GiB)
            {
                return Fat32SpcUpTo32GiB;
            }
            // ≥ 32 GiB → SPC 64 (32 KiB).
            return Fat32SpcAbove32GiB;
        }

        // FAT12/FAT16: 8 sectors/cluster keeps a few-MiB image inside the band
        // and is fine up through the FAT16 ceiling (~2 GiB at SPC=64). FAT16
        // on huge volumes is uncommon enough not to warrant a table.
        return Fat1216DefaultSectorsPerCluster;
    }

    private static uint ComputeFatSectors(
        FatType type,
        uint totalSectors,
        uint bytesPerSector,
        ushort reserved,
        byte numFats,
        byte spc,
        ushort rootEntries)
    {
        // Microsoft fatgen103 § 6.2: solve for FatSz so that
        //   (TotSec - Reserved - RootDir) / SPC <= (FatSz * BytesPerSec) / EntrySize - 2
        // We use the closed-form approximation with a +1-sector slack.
        uint rootDirSectors = (uint)(rootEntries * (uint)FatDirectory.EntrySize + (bytesPerSector - 1)) / bytesPerSector;
        if (totalSectors <= reserved + rootDirSectors)
        {
            return 0;
        }
        uint dataSectors = totalSectors - reserved - rootDirSectors;

        uint denom;
        if (type == FatType.Fat16 || type == FatType.Fat32)
        {
            // FAT16 entries are 2 bytes -> BytesPerSec/2 per sector;
            // fatgen103 §6.2 halves the term again for 4-byte FAT32
            // entries (128 per 512-byte sector, not 256).
            denom = ((bytesPerSector / FatTable.Fat16EntrySize) * spc) + numFats;
            if (type == FatType.Fat32)
            {
                denom /= FatTable.Fat32EntrySize / FatTable.Fat16EntrySize;
            }
        }
        else
        {
            // FAT12 (and the Unknown first pass): 1.5 bytes/entry ->
            // ~341 entries per 512-byte sector. Safe overestimate.
            denom = ((bytesPerSector * FatTable.Fat12EntriesPerPair / FatTable.Fat12PairBytes) * spc) + numFats;
        }

        uint fatSize = (dataSectors + denom - 1) / denom;
        if (fatSize == 0)
        {
            fatSize = 1;
        }
        return fatSize;
    }

    private static bool ValidateBand(FatType type, uint clusterCount)
    {
        return type switch
        {
            FatType.Fat12 => clusterCount > 0 && clusterCount <= Fat12MaxClusters,
            FatType.Fat16 => clusterCount > Fat12MaxClusters && clusterCount <= Fat16MaxClusters,
            FatType.Fat32 => clusterCount > Fat16MaxClusters,
            _ => false,
        };
    }

    private static string PadLabel(string label)
    {
        if (label.Length >= VolumeLabelLength)
        {
            return label.Substring(0, VolumeLabelLength);
        }
        return label.PadRight(VolumeLabelLength, ' ');
    }

    private static void WriteBootSector(IBlockDevice device, FormatGeometry g)
    {
        Span<byte> bpb = new byte[(int)g.BytesPerSector];

        bpb[JmpBootOffset] = JmpBootShortJumpOpcode;
        bpb[JmpBootDisplacementOffset] = g.Type == FatType.Fat32 ? Fat32JmpBootDisplacement : Fat1216JmpBootDisplacement;
        bpb[JmpBootNopOffset] = JmpBootNopOpcode;

        ReadOnlySpan<byte> oem = "MSWIN4.1"u8;
        oem.CopyTo(bpb.Slice(OemNameOffset, OemNameLength));

        BitConverter.TryWriteBytes(bpb.Slice(FatBootSector.BytsPerSecOffset, FatBootSector.UInt16FieldSize), (ushort)g.BytesPerSector);
        bpb[FatBootSector.SecPerClusOffset] = g.SectorsPerCluster;
        BitConverter.TryWriteBytes(bpb.Slice(FatBootSector.RsvdSecCntOffset, FatBootSector.UInt16FieldSize), g.ReservedSectorCount);
        bpb[FatBootSector.NumFatsOffset] = g.NumberOfFats;
        BitConverter.TryWriteBytes(bpb.Slice(FatBootSector.RootEntCntOffset, FatBootSector.UInt16FieldSize), g.RootEntryCount);
        // fatgen103: FAT12/16 volumes whose count fits 16 bits store it in
        // TotSec16 (strict drivers and fsck.fat read only that field
        // there) with TotSec32 zero; FAT32 and larger volumes use TotSec32.
        bool useTotSec16 = g.Type != FatType.Fat32 && g.TotalSectorCount <= ushort.MaxValue;
        BitConverter.TryWriteBytes(bpb.Slice(FatBootSector.TotSec16Offset, FatBootSector.UInt16FieldSize), useTotSec16 ? (ushort)g.TotalSectorCount : (ushort)0);
        bpb[MediaOffset] = MediaDescriptorFixed;
        BitConverter.TryWriteBytes(bpb.Slice(FatBootSector.FatSz16Offset, FatBootSector.UInt16FieldSize), g.Type == FatType.Fat32 ? (ushort)0 : (ushort)g.FatSectorCount);
        BitConverter.TryWriteBytes(bpb.Slice(SecPerTrkOffset, FatBootSector.UInt16FieldSize), LegacySectorsPerTrack); // CHS sectors/track (unused by LBA)
        BitConverter.TryWriteBytes(bpb.Slice(NumHeadsOffset, FatBootSector.UInt16FieldSize), LegacyHeadCount); // CHS heads (unused by LBA)
        BitConverter.TryWriteBytes(bpb.Slice(HiddSecOffset, FatBootSector.UInt32FieldSize), (uint)0);
        BitConverter.TryWriteBytes(bpb.Slice(FatBootSector.TotSec32Offset, FatBootSector.UInt32FieldSize), useTotSec16 ? 0u : g.TotalSectorCount);

        if (g.Type == FatType.Fat32)
        {
            BitConverter.TryWriteBytes(bpb.Slice(FatBootSector.FatSz32Offset, FatBootSector.UInt32FieldSize), g.FatSectorCount);
            BitConverter.TryWriteBytes(bpb.Slice(ExtFlagsOffset, FatBootSector.UInt16FieldSize), (ushort)0); // ext flags
            BitConverter.TryWriteBytes(bpb.Slice(FsVerOffset, FatBootSector.UInt16FieldSize), (ushort)0); // version
            BitConverter.TryWriteBytes(bpb.Slice(FatBootSector.RootClusOffset, FatBootSector.UInt32FieldSize), g.RootCluster);
            BitConverter.TryWriteBytes(bpb.Slice(FsInfoOffset, FatBootSector.UInt16FieldSize), FsInfoSectorNumber);
            BitConverter.TryWriteBytes(bpb.Slice(BkBootSecOffset, FatBootSector.UInt16FieldSize), BackupBootSectorNumber);

            bpb[Fat32DrvNumOffset] = DriveNumberFixedDisk;
            bpb[Fat32BootSigOffset] = ExtendedBootSignature;
            BitConverter.TryWriteBytes(bpb.Slice(Fat32VolIdOffset, FatBootSector.UInt32FieldSize), g.Serial);
            ReadOnlySpan<char> labelChars = g.Label.AsSpan();
            for (int i = 0; i < VolumeLabelLength && i < labelChars.Length; i++)
            {
                bpb[Fat32VolLabOffset + i] = (byte)labelChars[i];
            }
            ReadOnlySpan<byte> fsType = "FAT32   "u8;
            fsType.CopyTo(bpb.Slice(Fat32FilSysTypeOffset, FilSysTypeLength));
        }
        else
        {
            bpb[Fat1216DrvNumOffset] = DriveNumberFixedDisk;
            bpb[Fat1216BootSigOffset] = ExtendedBootSignature;
            BitConverter.TryWriteBytes(bpb.Slice(Fat1216VolIdOffset, FatBootSector.UInt32FieldSize), g.Serial);
            ReadOnlySpan<char> labelChars = g.Label.AsSpan();
            for (int i = 0; i < VolumeLabelLength && i < labelChars.Length; i++)
            {
                bpb[Fat1216VolLabOffset + i] = (byte)labelChars[i];
            }
            ReadOnlySpan<byte> fsType = g.Type == FatType.Fat12 ? "FAT12   "u8 : "FAT16   "u8;
            fsType.CopyTo(bpb.Slice(Fat1216FilSysTypeOffset, FilSysTypeLength));
        }

        BitConverter.TryWriteBytes(bpb.Slice(FatBootSector.BootSignatureOffset, FatBootSector.UInt16FieldSize), FatBootSector.BootSignature);

        device.WriteBlock(FatBootSector.BootSectorLba, 1, bpb);
    }

    private static void WriteFat32FsInfo(IBlockDevice device, FormatGeometry g)
    {
        Span<byte> sector = new byte[(int)g.BytesPerSector];
        BitConverter.TryWriteBytes(sector.Slice(FsiLeadSigOffset, FatBootSector.UInt32FieldSize), FsiLeadSig);
        BitConverter.TryWriteBytes(sector.Slice(FsiStrucSigOffset, FatBootSector.UInt32FieldSize), FsiStrucSig);
        BitConverter.TryWriteBytes(sector.Slice(FsiFreeCountOffset, FatBootSector.UInt32FieldSize), FsiUnknownValue);
        BitConverter.TryWriteBytes(sector.Slice(FsiNextFreeOffset, FatBootSector.UInt32FieldSize), FsiUnknownValue);
        BitConverter.TryWriteBytes(sector.Slice(FsiTrailSigOffset, FatBootSector.UInt32FieldSize), FsiTrailSig);
        device.WriteBlock(FsInfoSectorNumber, 1, sector);
    }

    private static void WriteFat32BackupBoot(IBlockDevice device, FormatGeometry g)
    {
        // Re-emit the boot sector at offset 6 so the backup matches.
        Span<byte> bpb = new byte[(int)g.BytesPerSector];
        device.ReadBlock(FatBootSector.BootSectorLba, 1, bpb);
        device.WriteBlock(BackupBootSectorNumber, 1, bpb);
    }

    private static void InitializeFats(IBlockDevice device, FormatGeometry g)
    {
        Span<byte> firstSector = new byte[(int)g.BytesPerSector];

        switch (g.Type)
        {
            case FatType.Fat12:
                // Entries 0 and 1 share one packed group: the media
                // descriptor takes its low byte, every remaining nibble
                // of the group reads all-ones.
                firstSector[0] = MediaDescriptorFixed;
                firstSector.Slice(1, (int)FatTable.Fat12PairBytes - 1).Fill(Fat12ReservedFill);
                break;
            case FatType.Fat16:
                BitConverter.TryWriteBytes(firstSector.Slice(0, (int)FatTable.Fat16EntrySize), Fat16Fat0Entry);
                BitConverter.TryWriteBytes(firstSector.Slice((int)FatTable.Fat16EntrySize, (int)FatTable.Fat16EntrySize), Fat16Fat1Entry);
                break;
            case FatType.Fat32:
                BitConverter.TryWriteBytes(firstSector.Slice(0, (int)FatTable.Fat32EntrySize), Fat32Fat0Entry);
                BitConverter.TryWriteBytes(firstSector.Slice((int)FatTable.Fat32EntrySize, (int)FatTable.Fat32EntrySize), Fat32EndOfChainEntry);
                // Mark the root cluster as end-of-chain.
                uint rootEntryByte = g.RootCluster * FatTable.Fat32EntrySize;
                if (rootEntryByte + FatTable.Fat32EntrySize <= g.BytesPerSector)
                {
                    BitConverter.TryWriteBytes(firstSector.Slice((int)rootEntryByte, (int)FatTable.Fat32EntrySize), Fat32EndOfChainEntry);
                }
                break;
        }

        for (uint fatIndex = 0; fatIndex < g.NumberOfFats; fatIndex++)
        {
            uint fatStart = (uint)g.ReservedSectorCount + fatIndex * g.FatSectorCount;
            device.WriteBlock(fatStart, 1, firstSector);
            if (g.FatSectorCount > 1)
            {
                ZeroSectors(device, fatStart + 1, g.FatSectorCount - 1, g.BytesPerSector);
            }

            // FAT32: if the root cluster's FAT entry didn't land in sector 0, write it where it does.
            if (g.Type == FatType.Fat32)
            {
                uint rootByte = g.RootCluster * FatTable.Fat32EntrySize;
                if (rootByte >= g.BytesPerSector)
                {
                    uint sectorOffset = rootByte / g.BytesPerSector;
                    uint inSector = rootByte % g.BytesPerSector;
                    // ResolveGeometry bounds RootCluster to the cluster
                    // count; keep the FAT-region bound as defense in depth.
                    if (sectorOffset < g.FatSectorCount)
                    {
                        Span<byte> rsec = new byte[(int)g.BytesPerSector];
                        BitConverter.TryWriteBytes(rsec.Slice((int)inSector, (int)FatTable.Fat32EntrySize), Fat32EndOfChainEntry);
                        device.WriteBlock(fatStart + sectorOffset, 1, rsec);
                    }
                }
            }
        }
    }

    private static void ZeroRootArea(IBlockDevice device, FormatGeometry g)
    {
        if (g.Type == FatType.Fat32)
        {
            uint rootSector = (uint)(g.ReservedSectorCount + g.NumberOfFats * g.FatSectorCount
                + (g.RootCluster - FatTable.FirstDataCluster) * g.SectorsPerCluster);
            ZeroSectors(device, rootSector, g.SectorsPerCluster, g.BytesPerSector);
        }
        else
        {
            uint rootStart = (uint)(g.ReservedSectorCount + g.NumberOfFats * g.FatSectorCount);
            ZeroSectors(device, rootStart, g.RootDirSectors, g.BytesPerSector);
        }
    }

    /// <summary>Zeroes <paramref name="count"/> sectors in <see cref="ZeroBatchSectors"/>-sized WriteBlock batches.</summary>
    private static void ZeroSectors(IBlockDevice device, uint startLba, uint count, uint bytesPerSector)
    {
        uint batchSectors = count < ZeroBatchSectors ? count : ZeroBatchSectors;
        if (batchSectors == 0)
        {
            return;
        }
        Span<byte> zero = new byte[(int)(bytesPerSector * batchSectors)];
        uint done = 0;
        while (done < count)
        {
            uint batch = count - done < batchSectors ? count - done : batchSectors;
            device.WriteBlock(startLba + done, batch, zero.Slice(0, (int)(batch * bytesPerSector)));
            done += batch;
        }
    }
}
