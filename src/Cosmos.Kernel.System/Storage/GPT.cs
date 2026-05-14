// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.HAL.Interfaces.Devices;

namespace Cosmos.Kernel.System.Storage;

/// <summary>
/// GPT (GUID Partition Table) parser / writer. Reads / writes the primary
/// header at LBA 1 plus the partition entry array starting at LBA 2.
/// 512-byte sectors assumed (matches every block device this kernel
/// currently exposes).
/// </summary>
public static class GPT
{
    /// <summary>"EFI PART" little-endian.</summary>
    private const ulong EfiPartSignature = 0x5452415020494645UL;

    /// <summary>Microsoft Basic Data Partition GUID — used by FAT/NTFS/exFAT volumes.</summary>
    public static readonly Guid BasicDataPartitionType = new(
        0xEBD0A0A2, 0xB9E5, 0x4433, 0x87, 0xC0, 0x68, 0xB6, 0xB7, 0x26, 0x99, 0xC7);

    /// <summary>Single parsed partition. Sector positions are absolute on the host disk.</summary>
    public sealed class PartitionEntry
    {
        public Guid PartitionType { get; }
        public Guid PartitionGuid { get; }
        public ulong StartSector { get; }
        public ulong SectorCount { get; }

        public PartitionEntry(Guid partitionType, Guid partitionGuid, ulong startSector, ulong sectorCount)
        {
            PartitionType = partitionType;
            PartitionGuid = partitionGuid;
            StartSector = startSector;
            SectorCount = sectorCount;
        }
    }

    /// <summary>True if the GPT header at LBA 1 starts with the EFI PART signature.</summary>
    public static bool IsGPT(IBlockDevice device)
    {
        Span<byte> header = new byte[device.BlockSize];
        device.ReadBlock(1, 1, header);
        return BitConverter.ToUInt64(header.Slice(0, 8)) == EfiPartSignature;
    }

    /// <summary>
    /// Walk the GPT partition entry array. Empty slots (zero PartitionType
    /// GUID) are skipped.
    /// </summary>
    public static List<PartitionEntry> Parse(IBlockDevice device)
    {
        List<PartitionEntry> partitions = new();
        ulong blockSize = device.BlockSize;

        Span<byte> header = new byte[blockSize];
        device.ReadBlock(1, 1, header);
        if (BitConverter.ToUInt64(header.Slice(0, 8)) != EfiPartSignature)
        {
            return partitions;
        }

        ulong entryStartLba = BitConverter.ToUInt64(header.Slice(72, 8));
        uint entryCount = BitConverter.ToUInt32(header.Slice(80, 4));
        uint entrySize = BitConverter.ToUInt32(header.Slice(84, 4));
        if (entrySize == 0)
        {
            return partitions;
        }

        uint entriesPerSector = (uint)(blockSize / entrySize);
        if (entriesPerSector == 0)
        {
            return partitions;
        }

        Span<byte> sector = new byte[blockSize];
        for (ulong s = 0; s < (ulong)entryCount; s += entriesPerSector)
        {
            device.ReadBlock(entryStartLba + s / entriesPerSector, 1, sector);
            uint thisSector = (uint)Math.Min((ulong)entriesPerSector, entryCount - s);
            for (uint j = 0; j < thisSector; j++)
            {
                int offset = (int)(j * entrySize);
                Guid partType = ReadGuid(sector.Slice(offset, 16));
                if (partType == Guid.Empty)
                {
                    continue;
                }

                Guid partGuid = ReadGuid(sector.Slice(offset + 16, 16));
                ulong startLba = BitConverter.ToUInt64(sector.Slice(offset + 32, 8));
                ulong endLba = BitConverter.ToUInt64(sector.Slice(offset + 40, 8));
                // endLba is inclusive.
                ulong count = endLba + 1 - startLba;

                partitions.Add(new PartitionEntry(partType, partGuid, startLba, count));
            }
        }

        return partitions;
    }

    /// <summary>
    /// Write a fresh GPT layout: protective MBR at LBA 0, primary header at
    /// LBA 1, and 32 zeroed sectors of 128-byte partition entries (LBAs
    /// 2..33). Backup header / array intentionally not written — callers
    /// targeting test images don't need them, and writing them needs CRC
    /// support that the boot path doesn't yet ship.
    /// </summary>
    public static void Create(IBlockDevice device)
    {
        ulong blockSize = device.BlockSize;

        // Protective MBR at LBA 0.
        Span<byte> protectiveMbr = new byte[blockSize];
        protectiveMbr[446 + 0] = 0x00; // boot indicator
        protectiveMbr[446 + 1] = 0x00; // start CHS head
        protectiveMbr[446 + 2] = 0x02; // start CHS sector
        protectiveMbr[446 + 3] = 0x00; // start CHS cylinder
        protectiveMbr[446 + 4] = 0xEE; // GPT protective system id
        protectiveMbr[446 + 5] = 0xFF; // end CHS head
        protectiveMbr[446 + 6] = 0xFF; // end CHS sector
        protectiveMbr[446 + 7] = 0xFF; // end CHS cylinder
        BitConverter.TryWriteBytes(protectiveMbr.Slice(446 + 8, 4), 1u);
        uint sizeInLba = device.BlockCount > 0xFFFFFFFFUL
            ? 0xFFFFFFFFu
            : (uint)(device.BlockCount - 1);
        BitConverter.TryWriteBytes(protectiveMbr.Slice(446 + 12, 4), sizeInLba);
        BitConverter.TryWriteBytes(protectiveMbr.Slice(510, 2), (ushort)0xAA55);
        device.WriteBlock(0, 1, protectiveMbr);

        // GPT header at LBA 1.
        Span<byte> header = new byte[blockSize];
        BitConverter.TryWriteBytes(header.Slice(0, 8), EfiPartSignature);
        BitConverter.TryWriteBytes(header.Slice(8, 4), 0x00010000u);  // Revision 1.0
        BitConverter.TryWriteBytes(header.Slice(12, 4), 92u);          // header size
        BitConverter.TryWriteBytes(header.Slice(16, 4), 0u);           // CRC32 (not computed; consumers that care will reject)
        BitConverter.TryWriteBytes(header.Slice(24, 8), 1UL);          // current LBA
        BitConverter.TryWriteBytes(header.Slice(32, 8), device.BlockCount - 1);  // backup LBA
        BitConverter.TryWriteBytes(header.Slice(40, 8), 34UL);         // first usable LBA
        BitConverter.TryWriteBytes(header.Slice(48, 8), device.BlockCount - 34); // last usable LBA
        // Disk GUID — deterministic, derived from disk size so identical inputs yield identical layouts.
        ulong sizeMix = device.BlockCount;
        WriteDeterministicGuid(header.Slice(56, 16), sizeMix);
        BitConverter.TryWriteBytes(header.Slice(72, 8), 2UL);          // partition entry array LBA
        BitConverter.TryWriteBytes(header.Slice(80, 4), 128u);         // entry count
        BitConverter.TryWriteBytes(header.Slice(84, 4), 128u);         // entry size
        BitConverter.TryWriteBytes(header.Slice(88, 4), 0u);           // entry array CRC32
        device.WriteBlock(1, 1, header);

        // Zero the partition entry array (LBAs 2..33 with 512B sectors and 128 entries × 128B).
        Span<byte> empty = new byte[blockSize];
        ulong entriesPerSector = blockSize / 128;
        ulong arraySectors = entriesPerSector == 0 ? 0 : (128 + entriesPerSector - 1) / entriesPerSector;
        for (ulong i = 0; i < arraySectors; i++)
        {
            device.WriteBlock(2 + i, 1, empty);
        }
    }

    /// <summary>
    /// Add a partition entry of <paramref name="partitionType"/> covering
    /// <paramref name="sectorCount"/> sectors starting at
    /// <paramref name="startSector"/>. Returns false if the GPT header is
    /// missing or no slot is free.
    /// </summary>
    public static bool AddPartition(IBlockDevice device, ulong startSector, ulong sectorCount, Guid partitionType)
    {
        ulong blockSize = device.BlockSize;
        Span<byte> header = new byte[blockSize];
        device.ReadBlock(1, 1, header);
        if (BitConverter.ToUInt64(header.Slice(0, 8)) != EfiPartSignature)
        {
            return false;
        }

        ulong entryStartLba = BitConverter.ToUInt64(header.Slice(72, 8));
        uint entryCount = BitConverter.ToUInt32(header.Slice(80, 4));
        uint entrySize = BitConverter.ToUInt32(header.Slice(84, 4));
        uint entriesPerSector = (uint)(blockSize / entrySize);
        if (entriesPerSector == 0)
        {
            return false;
        }

        Span<byte> sector = new byte[blockSize];
        for (uint s = 0; s < entryCount; s += entriesPerSector)
        {
            ulong lba = entryStartLba + s / entriesPerSector;
            device.ReadBlock(lba, 1, sector);
            uint thisSector = (uint)Math.Min((ulong)entriesPerSector, entryCount - s);
            for (uint j = 0; j < thisSector; j++)
            {
                int offset = (int)(j * entrySize);
                if (!IsZero(sector.Slice(offset, 16)))
                {
                    continue;
                }

                WriteGuid(sector.Slice(offset, 16), partitionType);
                ulong slotIdx = s + j;
                ulong guidMix = startSector ^ sectorCount ^ slotIdx;
                WriteDeterministicGuid(sector.Slice(offset + 16, 16), guidMix);
                BitConverter.TryWriteBytes(sector.Slice(offset + 32, 8), startSector);
                BitConverter.TryWriteBytes(sector.Slice(offset + 40, 8), startSector + sectorCount - 1);
                BitConverter.TryWriteBytes(sector.Slice(offset + 48, 8), 0UL);

                device.WriteBlock(lba, 1, sector);
                return true;
            }
        }

        return false;
    }

    private static Guid ReadGuid(Span<byte> source)
    {
        byte[] bytes = new byte[16];
        source.Slice(0, 16).CopyTo(bytes);
        return new Guid(bytes);
    }

    private static void WriteGuid(Span<byte> dest, Guid value)
    {
        byte[] bytes = value.ToByteArray();
        bytes.AsSpan().CopyTo(dest);
    }

    private static void WriteDeterministicGuid(Span<byte> dest, ulong mix)
    {
        BitConverter.TryWriteBytes(dest.Slice(0, 4), (uint)mix);
        BitConverter.TryWriteBytes(dest.Slice(4, 4), (uint)(mix ^ 0x12345678UL));
        BitConverter.TryWriteBytes(dest.Slice(8, 4), (uint)(mix ^ 0x87654321UL));
        BitConverter.TryWriteBytes(dest.Slice(12, 4), (uint)(mix ^ 0xDEADBEEFUL));
    }

    private static bool IsZero(Span<byte> data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] != 0)
            {
                return false;
            }
        }
        return true;
    }
}
