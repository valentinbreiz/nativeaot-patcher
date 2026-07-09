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
    /// <summary>Boot signature stamped at bytes 510-511 of the boot sector.</summary>
    private const ushort BootSignature = 0xAA55;

    /// <summary>Byte offset of the 0xAA55 boot signature.</summary>
    private const int BootSignatureOffset = 510;

    /// <summary>Highest cluster count of the FAT12 band (fatgen103 §3.5).</summary>
    private const uint Fat12MaxClusters = 4084;

    /// <summary>Highest cluster count of the FAT16 band (fatgen103 §3.5).</summary>
    private const uint Fat16MaxClusters = 65524;

    /// <summary>Smallest sector size the FAT spec permits (and the BPB layout assumes).</summary>
    private const uint MinBytesPerSector = 512;

    /// <summary>Largest sector size the FAT spec permits; larger would truncate the 16-bit BPB field.</summary>
    private const uint MaxBytesPerSector = 4096;

    /// <summary>Size in bytes of one directory entry.</summary>
    private const uint DirEntrySize = 32;

    /// <summary>FAT32 FAT entry width in bytes (4; FAT16 is 2, FAT12 is 1.5).</summary>
    private const uint Fat32EntrySize = 4;

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

    /// <summary>Sectors zeroed per WriteBlock batch when clearing FAT / root areas.</summary>
    private const uint ZeroBatchSectors = 64;

    public static bool Format(IBlockDevice device, FatFormatOptions? options)
    {
        if (device == null || device.BlockCount < 8)
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

        public uint RootDirSectors => (uint)(RootEntryCount * DirEntrySize + (BytesPerSector - 1)) / BytesPerSector;
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
        if (bytesPerSector < MinBytesPerSector || bytesPerSector > MaxBytesPerSector
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
            : (ushort)(type == FatType.Fat32 ? 32 : 1);
        byte numFats = opts.NumberOfFats == 0 ? (byte)2 : opts.NumberOfFats;
        ushort rootEntries = type == FatType.Fat32
            ? (ushort)0
            : (opts.RootEntryCount != 0 ? opts.RootEntryCount : (ushort)512);
        uint rootCluster = type == FatType.Fat32
            ? (opts.RootCluster != 0 ? opts.RootCluster : 2u)
            : 0u;

        uint fatSectors = opts.FatSectorCount != 0
            ? opts.FatSectorCount
            : ComputeFatSectors(type, totalSectors, bytesPerSector, reserved, numFats, spc, rootEntries);
        if (fatSectors == 0)
        {
            return false;
        }

        // Decide / validate type from cluster count.
        uint rootDirSectors = (uint)(rootEntries * DirEntrySize + (bytesPerSector - 1)) / bytesPerSector;
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
                reserved = opts.ReservedSectorCount != 0 ? opts.ReservedSectorCount : (ushort)32;
                rootEntries = 0;
                rootCluster = opts.RootCluster != 0 ? opts.RootCluster : 2u;
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
        if (resolved == FatType.Fat32 && (rootCluster < 2 || rootCluster >= clusterCount + 2))
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
            if (totalSectors < 532_480)
            {
                return 1;
            }
            // < 8 GiB → SPC 8 (4 KiB cluster).
            if (totalSectors < 16_777_216)
            {
                return 8;
            }
            // < 16 GiB → SPC 16 (8 KiB).
            if (totalSectors < 33_554_432)
            {
                return 16;
            }
            // < 32 GiB → SPC 32 (16 KiB).
            if (totalSectors < 67_108_864)
            {
                return 32;
            }
            // ≥ 32 GiB → SPC 64 (32 KiB).
            return 64;
        }

        // FAT12/FAT16: 8 sectors/cluster keeps a few-MiB image inside the band
        // and is fine up through the FAT16 ceiling (~2 GiB at SPC=64). FAT16
        // on huge volumes is uncommon enough not to warrant a table.
        return 8;
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
        uint rootDirSectors = (uint)(rootEntries * DirEntrySize + (bytesPerSector - 1)) / bytesPerSector;
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
            denom = ((bytesPerSector / 2u) * spc) + numFats;
            if (type == FatType.Fat32)
            {
                denom /= 2;
            }
        }
        else
        {
            // FAT12 (and the Unknown first pass): 1.5 bytes/entry ->
            // ~341 entries per 512-byte sector. Safe overestimate.
            denom = ((bytesPerSector * 2u / 3u) * spc) + numFats;
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
        if (label.Length >= 11)
        {
            return label.Substring(0, 11);
        }
        return label.PadRight(11, ' ');
    }

    private static void WriteBootSector(IBlockDevice device, FormatGeometry g)
    {
        Span<byte> bpb = new byte[(int)g.BytesPerSector];

        bpb[0] = 0xEB;
        bpb[1] = (byte)(g.Type == FatType.Fat32 ? 0x58 : 0x3C);
        bpb[2] = 0x90;

        ReadOnlySpan<byte> oem = "MSWIN4.1"u8;
        oem.CopyTo(bpb.Slice(3, 8));

        BitConverter.TryWriteBytes(bpb.Slice(11, 2), (ushort)g.BytesPerSector);
        bpb[13] = g.SectorsPerCluster;
        BitConverter.TryWriteBytes(bpb.Slice(14, 2), g.ReservedSectorCount);
        bpb[16] = g.NumberOfFats;
        BitConverter.TryWriteBytes(bpb.Slice(17, 2), g.RootEntryCount);
        // fatgen103: FAT12/16 volumes whose count fits 16 bits store it in
        // TotSec16 (strict drivers and fsck.fat read only that field
        // there) with TotSec32 zero; FAT32 and larger volumes use TotSec32.
        bool useTotSec16 = g.Type != FatType.Fat32 && g.TotalSectorCount <= ushort.MaxValue;
        BitConverter.TryWriteBytes(bpb.Slice(19, 2), useTotSec16 ? (ushort)g.TotalSectorCount : (ushort)0);
        bpb[21] = MediaDescriptorFixed;
        BitConverter.TryWriteBytes(bpb.Slice(22, 2), g.Type == FatType.Fat32 ? (ushort)0 : (ushort)g.FatSectorCount);
        BitConverter.TryWriteBytes(bpb.Slice(24, 2), (ushort)63); // CHS sectors/track (unused by LBA)
        BitConverter.TryWriteBytes(bpb.Slice(26, 2), (ushort)16); // CHS heads (unused by LBA)
        BitConverter.TryWriteBytes(bpb.Slice(28, 4), (uint)0);
        BitConverter.TryWriteBytes(bpb.Slice(32, 4), useTotSec16 ? 0u : g.TotalSectorCount);

        if (g.Type == FatType.Fat32)
        {
            BitConverter.TryWriteBytes(bpb.Slice(36, 4), g.FatSectorCount);
            BitConverter.TryWriteBytes(bpb.Slice(40, 2), (ushort)0); // ext flags
            BitConverter.TryWriteBytes(bpb.Slice(42, 2), (ushort)0); // version
            BitConverter.TryWriteBytes(bpb.Slice(44, 4), g.RootCluster);
            BitConverter.TryWriteBytes(bpb.Slice(48, 2), FsInfoSectorNumber);
            BitConverter.TryWriteBytes(bpb.Slice(50, 2), BackupBootSectorNumber);

            bpb[64] = DriveNumberFixedDisk;
            bpb[66] = ExtendedBootSignature;
            BitConverter.TryWriteBytes(bpb.Slice(67, 4), g.Serial);
            ReadOnlySpan<char> labelChars = g.Label.AsSpan();
            for (int i = 0; i < 11 && i < labelChars.Length; i++)
            {
                bpb[71 + i] = (byte)labelChars[i];
            }
            ReadOnlySpan<byte> fsType = "FAT32   "u8;
            fsType.CopyTo(bpb.Slice(82, 8));
        }
        else
        {
            bpb[36] = DriveNumberFixedDisk;
            bpb[38] = ExtendedBootSignature;
            BitConverter.TryWriteBytes(bpb.Slice(39, 4), g.Serial);
            ReadOnlySpan<char> labelChars = g.Label.AsSpan();
            for (int i = 0; i < 11 && i < labelChars.Length; i++)
            {
                bpb[43 + i] = (byte)labelChars[i];
            }
            ReadOnlySpan<byte> fsType = g.Type == FatType.Fat12 ? "FAT12   "u8 : "FAT16   "u8;
            fsType.CopyTo(bpb.Slice(54, 8));
        }

        BitConverter.TryWriteBytes(bpb.Slice(BootSignatureOffset, 2), BootSignature);

        device.WriteBlock(0, 1, bpb);
    }

    private static void WriteFat32FsInfo(IBlockDevice device, FormatGeometry g)
    {
        Span<byte> sector = new byte[(int)g.BytesPerSector];
        BitConverter.TryWriteBytes(sector.Slice(0, 4), 0x41615252u);
        BitConverter.TryWriteBytes(sector.Slice(484, 4), 0x61417272u);
        BitConverter.TryWriteBytes(sector.Slice(488, 4), 0xFFFFFFFFu);
        BitConverter.TryWriteBytes(sector.Slice(492, 4), 0xFFFFFFFFu);
        BitConverter.TryWriteBytes(sector.Slice(508, 4), 0xAA550000u);
        device.WriteBlock(1, 1, sector);
    }

    private static void WriteFat32BackupBoot(IBlockDevice device, FormatGeometry g)
    {
        // Re-emit the boot sector at offset 6 so the backup matches.
        Span<byte> bpb = new byte[(int)g.BytesPerSector];
        device.ReadBlock(0, 1, bpb);
        device.WriteBlock(6, 1, bpb);
    }

    private static void InitializeFats(IBlockDevice device, FormatGeometry g)
    {
        Span<byte> firstSector = new byte[(int)g.BytesPerSector];

        switch (g.Type)
        {
            case FatType.Fat12:
                firstSector[0] = 0xF8;
                firstSector[1] = 0xFF;
                firstSector[2] = 0xFF;
                break;
            case FatType.Fat16:
                BitConverter.TryWriteBytes(firstSector.Slice(0, 2), (ushort)0xFFF8);
                BitConverter.TryWriteBytes(firstSector.Slice(2, 2), (ushort)0xFFFF);
                break;
            case FatType.Fat32:
                BitConverter.TryWriteBytes(firstSector.Slice(0, 4), 0x0FFFFFF8u);
                BitConverter.TryWriteBytes(firstSector.Slice(4, 4), 0x0FFFFFFFu);
                // Mark the root cluster as end-of-chain.
                uint rootEntryByte = g.RootCluster * Fat32EntrySize;
                if (rootEntryByte + Fat32EntrySize <= g.BytesPerSector)
                {
                    BitConverter.TryWriteBytes(firstSector.Slice((int)rootEntryByte, (int)Fat32EntrySize), 0x0FFFFFFFu);
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
                uint rootByte = g.RootCluster * Fat32EntrySize;
                if (rootByte >= g.BytesPerSector)
                {
                    uint sectorOffset = rootByte / g.BytesPerSector;
                    uint inSector = rootByte % g.BytesPerSector;
                    // ResolveGeometry bounds RootCluster to the cluster
                    // count; keep the FAT-region bound as defense in depth.
                    if (sectorOffset < g.FatSectorCount)
                    {
                        Span<byte> rsec = new byte[(int)g.BytesPerSector];
                        BitConverter.TryWriteBytes(rsec.Slice((int)inSector, (int)Fat32EntrySize), 0x0FFFFFFFu);
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
            uint rootSector = (uint)(g.ReservedSectorCount + g.NumberOfFats * g.FatSectorCount + (g.RootCluster - 2) * g.SectorsPerCluster);
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
