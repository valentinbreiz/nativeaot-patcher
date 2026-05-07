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
    private const ushort BootSignature = 0xAA55;
    private const uint Fat12MaxClusters = 4084;
    private const uint Fat16MaxClusters = 65524;

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

        return true;
    }

    public static bool Destroy(IBlockDevice device)
    {
        if (device == null || device.BlockCount < 1)
        {
            return false;
        }

        Span<byte> zero = new byte[(int)device.BlockSize];
        device.WriteBlock(0, 1, zero);
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

        public uint RootDirSectors => (uint)(RootEntryCount * 32u + (BytesPerSector - 1)) / BytesPerSector;
        public uint FatRegion => NumberOfFats * FatSectorCount;
        public uint DataStart => Type == FatType.Fat32
            ? ReservedSectorCount + FatRegion
            : ReservedSectorCount + FatRegion + RootDirSectors;
    }

    private static bool ResolveGeometry(IBlockDevice device, FatFormatOptions opts, out FormatGeometry geom)
    {
        geom = default;

        uint bytesPerSector = (uint)device.BlockSize;
        if (bytesPerSector == 0 || device.BlockCount > uint.MaxValue)
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
        uint rootDirSectors = (uint)(rootEntries * 32u + (bytesPerSector - 1)) / bytesPerSector;
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
            resolved = clusterCount < 4085 ? FatType.Fat12
                : (clusterCount < 65525 ? FatType.Fat16 : FatType.Fat32);
            // FAT32 needs a different layout (reserved=32, no root dir region).
            if (resolved == FatType.Fat32)
            {
                reserved = opts.ReservedSectorCount != 0 ? opts.ReservedSectorCount : (ushort)32;
                rootEntries = 0;
                rootCluster = opts.RootCluster != 0 ? opts.RootCluster : 2u;
                rootDirSectors = 0;
                fatSectors = opts.FatSectorCount != 0
                    ? opts.FatSectorCount
                    : ComputeFatSectors(FatType.Fat32, totalSectors, bytesPerSector, reserved, numFats, spc, 0);
                fatRegion = (uint)numFats * fatSectors;
                dataStart = reserved + fatRegion;
                if (totalSectors <= dataStart)
                {
                    return false;
                }
                clusterCount = (totalSectors - dataStart) / spc;
            }
        }
        else
        {
            if (!ValidateBand(resolved, clusterCount))
            {
                return false;
            }
        }

        string label = string.IsNullOrEmpty(opts.VolumeLabel) ? "NO NAME    " : PadLabel(opts.VolumeLabel!);
        uint serial = opts.VolumeSerial != 0 ? opts.VolumeSerial : 0xC051D2A3u;

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
        uint rootDirSectors = (uint)(rootEntries * 32u + (bytesPerSector - 1)) / bytesPerSector;
        if (totalSectors <= reserved + rootDirSectors)
        {
            return 0;
        }
        uint dataSectors = totalSectors - reserved - rootDirSectors;

        uint denom;
        if (type == FatType.Fat32)
        {
            denom = (256u * spc) + numFats;
        }
        else if (type == FatType.Fat16)
        {
            denom = (256u * spc) + numFats;
            // FAT16 entry is 2 bytes -> 256 entries per 512-byte sector. Same denom shape.
        }
        else
        {
            // FAT12: 1.5 bytes/entry -> ~341 entries per 512-byte sector. Use a safe overestimate.
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
        BitConverter.TryWriteBytes(bpb.Slice(19, 2), (ushort)0); // total16: always 0, use total32
        bpb[21] = 0xF8;
        BitConverter.TryWriteBytes(bpb.Slice(22, 2), g.Type == FatType.Fat32 ? (ushort)0 : (ushort)g.FatSectorCount);
        BitConverter.TryWriteBytes(bpb.Slice(24, 2), (ushort)63);
        BitConverter.TryWriteBytes(bpb.Slice(26, 2), (ushort)16);
        BitConverter.TryWriteBytes(bpb.Slice(28, 4), (uint)0);
        BitConverter.TryWriteBytes(bpb.Slice(32, 4), g.TotalSectorCount);

        if (g.Type == FatType.Fat32)
        {
            BitConverter.TryWriteBytes(bpb.Slice(36, 4), g.FatSectorCount);
            BitConverter.TryWriteBytes(bpb.Slice(40, 2), (ushort)0); // ext flags
            BitConverter.TryWriteBytes(bpb.Slice(42, 2), (ushort)0); // version
            BitConverter.TryWriteBytes(bpb.Slice(44, 4), g.RootCluster);
            BitConverter.TryWriteBytes(bpb.Slice(48, 2), (ushort)1); // FSInfo
            BitConverter.TryWriteBytes(bpb.Slice(50, 2), (ushort)6); // backup boot

            bpb[64] = 0x80;
            bpb[66] = 0x29;
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
            bpb[36] = 0x80;
            bpb[38] = 0x29;
            BitConverter.TryWriteBytes(bpb.Slice(39, 4), g.Serial);
            ReadOnlySpan<char> labelChars = g.Label.AsSpan();
            for (int i = 0; i < 11 && i < labelChars.Length; i++)
            {
                bpb[43 + i] = (byte)labelChars[i];
            }
            ReadOnlySpan<byte> fsType = g.Type == FatType.Fat12 ? "FAT12   "u8 : "FAT16   "u8;
            fsType.CopyTo(bpb.Slice(54, 8));
        }

        bpb[510] = 0x55;
        bpb[511] = 0xAA;

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
        Span<byte> zero = new byte[(int)g.BytesPerSector];

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
                uint rootEntryByte = g.RootCluster * 4u;
                if (rootEntryByte + 4 <= g.BytesPerSector)
                {
                    BitConverter.TryWriteBytes(firstSector.Slice((int)rootEntryByte, 4), 0x0FFFFFFFu);
                }
                break;
        }

        for (uint fatIndex = 0; fatIndex < g.NumberOfFats; fatIndex++)
        {
            uint fatStart = (uint)g.ReservedSectorCount + fatIndex * g.FatSectorCount;
            device.WriteBlock(fatStart, 1, firstSector);
            for (uint i = 1; i < g.FatSectorCount; i++)
            {
                device.WriteBlock(fatStart + i, 1, zero);
            }

            // FAT32: if the root cluster's FAT entry didn't land in sector 0, write it where it does.
            if (g.Type == FatType.Fat32)
            {
                uint rootByte = g.RootCluster * 4u;
                if (rootByte >= g.BytesPerSector)
                {
                    uint sectorOffset = rootByte / g.BytesPerSector;
                    uint inSector = rootByte % g.BytesPerSector;
                    Span<byte> rsec = new byte[(int)g.BytesPerSector];
                    BitConverter.TryWriteBytes(rsec.Slice((int)inSector, 4), 0x0FFFFFFFu);
                    device.WriteBlock(fatStart + sectorOffset, 1, rsec);
                }
            }
        }
    }

    private static void ZeroRootArea(IBlockDevice device, FormatGeometry g)
    {
        Span<byte> zero = new byte[(int)g.BytesPerSector];

        if (g.Type == FatType.Fat32)
        {
            uint rootSector = (uint)(g.ReservedSectorCount + g.NumberOfFats * g.FatSectorCount + (g.RootCluster - 2) * g.SectorsPerCluster);
            for (uint i = 0; i < g.SectorsPerCluster; i++)
            {
                device.WriteBlock(rootSector + i, 1, zero);
            }
        }
        else
        {
            uint rootDirSectors = g.RootDirSectors;
            uint rootStart = (uint)(g.ReservedSectorCount + g.NumberOfFats * g.FatSectorCount);
            for (uint i = 0; i < rootDirSectors; i++)
            {
                device.WriteBlock(rootStart + i, 1, zero);
            }
        }
    }
}
