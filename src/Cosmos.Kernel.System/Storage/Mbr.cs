// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.HAL.Interfaces.Devices;

namespace Cosmos.Kernel.System.Storage;

/// <summary>
/// MBR (Master Boot Record) partition table parser / writer. Operates on
/// any <see cref="IBlockDevice"/> with 512-byte sectors.
/// </summary>
public static class Mbr
{
    private const ushort MbrSignature = 0xAA55;
    private const int PartitionTableOffset = 446;
    private const int PartitionEntrySize = 16;

    /// <summary>Single MBR partition entry. Sector positions are absolute on the host disk.</summary>
    public sealed class PartitionEntry
    {
        /// <summary>MBR partition type byte (e.g. 0x83 Linux, 0x0B FAT32).</summary>
        public byte SystemId { get; }

        /// <summary>First absolute LBA of the partition on the host disk.</summary>
        public ulong StartSector { get; }

        /// <summary>Length of the partition in sectors.</summary>
        public ulong SectorCount { get; }

        /// <summary>Creates an MBR partition entry.</summary>
        public PartitionEntry(byte systemId, ulong startSector, ulong sectorCount)
        {
            SystemId = systemId;
            StartSector = startSector;
            SectorCount = sectorCount;
        }
    }

    /// <summary>True if LBA 0 ends with the 0xAA55 MBR signature.</summary>
    public static bool IsMbr(IBlockDevice device)
    {
        Span<byte> mbr = new byte[device.BlockSize];
        device.ReadBlock(0, 1, mbr);
        return BitConverter.ToUInt16(mbr.Slice(510, 2)) == MbrSignature;
    }

    /// <summary>
    /// Parse the MBR's four primary partition entries. Empty (SystemId=0)
    /// entries are skipped. Extended (0x05/0x0F/0x85) entries are skipped
    /// here — logical-volume walking lives in a future EBR helper.
    /// </summary>
    public static List<PartitionEntry> Parse(IBlockDevice device)
    {
        List<PartitionEntry> partitions = new(4);
        Span<byte> mbr = new byte[device.BlockSize];
        device.ReadBlock(0, 1, mbr);

        for (int i = 0; i < 4; i++)
        {
            int offset = PartitionTableOffset + i * PartitionEntrySize;
            byte status = mbr[offset];
            byte systemId = mbr[offset + 4];
            // Skip empty and extended (EBR) entries, and 0xEE protective/
            // hybrid GPT entries — the real table is the GPT; registering
            // the protective entry as a data partition (e.g. when the
            // primary GPT header is damaged) would let a write destroy the
            // remaining GPT structures.
            if (systemId == 0 || systemId == 0x05 || systemId == 0x0F || systemId == 0x85 || systemId == 0xEE)
            {
                continue;
            }
            // The status byte must be 0x00 (inactive) or 0x80 (bootable);
            // anything else marks a corrupt entry.
            if (status != 0x00 && status != 0x80)
            {
                continue;
            }

            ulong startSector = BitConverter.ToUInt32(mbr.Slice(offset + 8, 4));
            ulong sectorCount = BitConverter.ToUInt32(mbr.Slice(offset + 12, 4));
            partitions.Add(new PartitionEntry(systemId, startSector, sectorCount));
        }

        return partitions;
    }

    /// <summary>
    /// Write a fresh, empty-but-signed MBR to LBA 0. No boot code, all
    /// four partition slots zeroed; callers add entries via
    /// <see cref="WritePartition"/>. Also wipes LBA 1 (wipefs-style):
    /// Create claims the whole disk label, and a stale "EFI PART" header
    /// left there from a previous GPT layout would keep winning over this
    /// MBR in the partition scanner, which checks GPT first.
    /// </summary>
    public static void Create(IBlockDevice device)
    {
        Span<byte> mbr = new byte[device.BlockSize];
        BitConverter.TryWriteBytes(mbr.Slice(510, 2), MbrSignature);
        device.WriteBlock(0, 1, mbr);

        Span<byte> lba1 = new byte[device.BlockSize];
        device.WriteBlock(1, 1, lba1);
    }

    /// <summary>
    /// Stamp partition <paramref name="index"/> (0..3) with a 32-bit
    /// LBA-addressed entry pointing at <paramref name="startSector"/> /
    /// <paramref name="sectorCount"/> with the given <paramref name="systemId"/>.
    /// </summary>
    public static void WritePartition(IBlockDevice device, int index, byte systemId, uint startSector, uint sectorCount)
    {
        if ((uint)index >= 4)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "MBR primary partition index must be 0..3.");
        }

        Span<byte> mbr = new byte[device.BlockSize];
        device.ReadBlock(0, 1, mbr);

        int offset = PartitionTableOffset + index * PartitionEntrySize;
        mbr.Slice(offset, PartitionEntrySize).Clear();
        mbr[offset + 4] = systemId;
        BitConverter.TryWriteBytes(mbr.Slice(offset + 8, 4), startSector);
        BitConverter.TryWriteBytes(mbr.Slice(offset + 12, 4), sectorCount);

        BitConverter.TryWriteBytes(mbr.Slice(510, 2), MbrSignature);
        device.WriteBlock(0, 1, mbr);
    }
}
