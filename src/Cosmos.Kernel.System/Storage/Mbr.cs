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

    /// <summary>Byte offset of the 0xAA55 boot signature within the MBR sector (bytes 510-511).</summary>
    private const int SignatureOffset = 510;

    /// <summary>Size in bytes of the 0xAA55 boot signature field.</summary>
    private const int SignatureSizeBytes = 2;

    /// <summary>Number of primary partition slots in an MBR partition table.</summary>
    private const int MaxPartitions = 4;

    /// <summary>Byte offset of the partition type (system ID) within a partition entry.</summary>
    private const int EntrySystemIdOffset = 4;

    /// <summary>Byte offset of the 32-bit starting LBA within a partition entry.</summary>
    private const int EntryStartLbaOffset = 8;

    /// <summary>Byte offset of the 32-bit sector count within a partition entry.</summary>
    private const int EntrySectorCountOffset = 12;

    /// <summary>Size in bytes of the 32-bit LBA / sector-count fields in a partition entry.</summary>
    private const int LbaFieldSizeBytes = 4;

    /// <summary>Partition status byte: inactive (non-bootable) entry.</summary>
    private const byte StatusInactive = 0x00;

    /// <summary>Partition status byte: active (bootable) entry.</summary>
    private const byte StatusBootable = 0x80;

    /// <summary>System ID 0x05 - extended partition (CHS-addressed EBR container).</summary>
    private const byte SystemIdExtendedChs = 0x05;

    /// <summary>System ID 0x0F - extended partition (LBA-addressed EBR container).</summary>
    private const byte SystemIdExtendedLba = 0x0F;

    /// <summary>System ID 0x85 - Linux extended partition (EBR container).</summary>
    private const byte SystemIdLinuxExtended = 0x85;

    /// <summary>System ID 0xEE - GPT protective/hybrid MBR entry (the real table is the GPT).</summary>
    private const byte SystemIdGptProtective = 0xEE;

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
        return BitConverter.ToUInt16(mbr.Slice(SignatureOffset, SignatureSizeBytes)) == MbrSignature;
    }

    /// <summary>
    /// Parse the MBR's four primary partition entries. Empty (SystemId=0)
    /// entries are skipped. Extended (0x05/0x0F/0x85) entries are skipped
    /// here — logical-volume walking lives in a future EBR helper.
    /// </summary>
    public static List<PartitionEntry> Parse(IBlockDevice device)
    {
        List<PartitionEntry> partitions = new(MaxPartitions);
        Span<byte> mbr = new byte[device.BlockSize];
        device.ReadBlock(0, 1, mbr);

        for (int i = 0; i < MaxPartitions; i++)
        {
            int offset = PartitionTableOffset + i * PartitionEntrySize;
            byte status = mbr[offset];
            byte systemId = mbr[offset + EntrySystemIdOffset];
            // Skip empty and extended (EBR) entries, and 0xEE protective/
            // hybrid GPT entries — the real table is the GPT; registering
            // the protective entry as a data partition (e.g. when the
            // primary GPT header is damaged) would let a write destroy the
            // remaining GPT structures.
            if (systemId == 0 || systemId == SystemIdExtendedChs || systemId == SystemIdExtendedLba || systemId == SystemIdLinuxExtended || systemId == SystemIdGptProtective)
            {
                continue;
            }
            // The status byte must be 0x00 (inactive) or 0x80 (bootable);
            // anything else marks a corrupt entry.
            if (status != StatusInactive && status != StatusBootable)
            {
                continue;
            }

            ulong startSector = BitConverter.ToUInt32(mbr.Slice(offset + EntryStartLbaOffset, LbaFieldSizeBytes));
            ulong sectorCount = BitConverter.ToUInt32(mbr.Slice(offset + EntrySectorCountOffset, LbaFieldSizeBytes));
            // Distrust on-disk metadata, mirroring the GPT hardening: start 0
            // aliases the MBR sector itself (a write through that "partition"
            // destroys the table), and a range past the disk end would
            // authorize wild host I/O through the resulting Partition.
            if (startSector == 0 || sectorCount == 0 || startSector + sectorCount > device.BlockCount)
            {
                continue;
            }
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
        BitConverter.TryWriteBytes(mbr.Slice(SignatureOffset, SignatureSizeBytes), MbrSignature);
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
        if ((uint)index >= MaxPartitions)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "MBR primary partition index must be 0..3.");
        }
        // Same rules Parse enforces on read: start 0 aliases the MBR sector
        // itself, and a range past the disk end would authorize wild host
        // I/O through the resulting Partition. Reject at write time rather
        // than stamping an entry our own parser will drop.
        if (startSector == 0 || sectorCount == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startSector), "MBR partition must start past LBA 0 and be non-empty.");
        }
        if (startSector + (ulong)sectorCount > device.BlockCount)
        {
            throw new ArgumentOutOfRangeException(nameof(sectorCount), "MBR partition extends past the end of the device.");
        }

        Span<byte> mbr = new byte[device.BlockSize];
        device.ReadBlock(0, 1, mbr);

        int offset = PartitionTableOffset + index * PartitionEntrySize;
        mbr.Slice(offset, PartitionEntrySize).Clear();
        mbr[offset + EntrySystemIdOffset] = systemId;
        BitConverter.TryWriteBytes(mbr.Slice(offset + EntryStartLbaOffset, LbaFieldSizeBytes), startSector);
        BitConverter.TryWriteBytes(mbr.Slice(offset + EntrySectorCountOffset, LbaFieldSizeBytes), sectorCount);

        BitConverter.TryWriteBytes(mbr.Slice(SignatureOffset, SignatureSizeBytes), MbrSignature);
        device.WriteBlock(0, 1, mbr);
    }
}
