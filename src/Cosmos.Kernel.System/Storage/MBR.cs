// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.HAL.Interfaces.Devices;

namespace Cosmos.Kernel.System.Storage;

/// <summary>
/// MBR (Master Boot Record) partition table parser / writer. Operates on
/// any <see cref="IBlockDevice"/> with 512-byte sectors.
/// </summary>
public static class MBR
{
    private const ushort MbrSignature = 0xAA55;
    private const int PartitionTableOffset = 446;
    private const int PartitionEntrySize = 16;

    /// <summary>Single MBR partition entry. Sector positions are absolute on the host disk.</summary>
    public sealed class PartitionEntry
    {
        public byte SystemId { get; }
        public ulong StartSector { get; }
        public ulong SectorCount { get; }

        public PartitionEntry(byte systemId, ulong startSector, ulong sectorCount)
        {
            SystemId = systemId;
            StartSector = startSector;
            SectorCount = sectorCount;
        }
    }

    /// <summary>True if LBA 0 ends with the 0xAA55 MBR signature.</summary>
    public static bool IsMBR(IBlockDevice device)
    {
        Span<byte> mbr = new byte[device.BlockSize];
        device.ReadBlock(0, 1, mbr);
        return BitConverter.ToUInt16(mbr.Slice(510, 2)) == MbrSignature;
    }

    /// <summary>
    /// Parse the MBR's four primary partition entries. Empty (SystemId=0)
    /// entries are skipped. Extended (0x05/0x0F/0x85) entries are skipped
    /// here; use <see cref="TryGetExtendedPartition"/> to walk logicals via
    /// <see cref="EBR"/>.
    /// </summary>
    public static List<PartitionEntry> Parse(IBlockDevice device)
    {
        List<PartitionEntry> partitions = new(4);
        Span<byte> mbr = new byte[device.BlockSize];
        device.ReadBlock(0, 1, mbr);

        for (int i = 0; i < 4; i++)
        {
            int offset = PartitionTableOffset + i * PartitionEntrySize;
            byte systemId = mbr[offset + 4];
            if (systemId == 0 || systemId == 0x05 || systemId == 0x0F || systemId == 0x85)
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
    /// Locate the first extended partition (system ID 0x05, 0x0F, or 0x85)
    /// in the MBR's primary table and return its absolute start LBA.
    /// </summary>
    public static bool TryGetExtendedPartition(IBlockDevice device, out ulong startSector)
    {
        return TryGetExtendedPartition(device, out startSector, out _);
    }

    /// <summary>
    /// As <see cref="TryGetExtendedPartition(IBlockDevice, out ulong)"/> but
    /// also returns the extended partition's sector count.
    /// </summary>
    public static bool TryGetExtendedPartition(IBlockDevice device, out ulong startSector, out ulong sectorCount)
    {
        startSector = 0;
        sectorCount = 0;
        Span<byte> mbr = new byte[device.BlockSize];
        device.ReadBlock(0, 1, mbr);

        for (int i = 0; i < 4; i++)
        {
            int offset = PartitionTableOffset + i * PartitionEntrySize;
            byte systemId = mbr[offset + 4];
            if (systemId == 0x05 || systemId == 0x0F || systemId == 0x85)
            {
                startSector = BitConverter.ToUInt32(mbr.Slice(offset + 8, 4));
                sectorCount = BitConverter.ToUInt32(mbr.Slice(offset + 12, 4));
                return startSector != 0;
            }
        }

        return false;
    }

    /// <summary>
    /// Write a fresh, empty-but-signed MBR to LBA 0. No boot code, all
    /// four partition slots zeroed; callers add entries via
    /// <see cref="WritePartition"/>.
    /// </summary>
    public static void Create(IBlockDevice device)
    {
        Span<byte> mbr = new byte[device.BlockSize];
        BitConverter.TryWriteBytes(mbr.Slice(510, 2), MbrSignature);
        device.WriteBlock(0, 1, mbr);
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

    /// <summary>Mark primary slot <paramref name="index"/> as empty by zeroing its 16-byte entry.</summary>
    public static void DeletePartition(IBlockDevice device, int index)
    {
        if ((uint)index >= 4)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "MBR primary partition index must be 0..3.");
        }

        Span<byte> mbr = new byte[device.BlockSize];
        device.ReadBlock(0, 1, mbr);

        int offset = PartitionTableOffset + index * PartitionEntrySize;
        mbr.Slice(offset, PartitionEntrySize).Clear();

        BitConverter.TryWriteBytes(mbr.Slice(510, 2), MbrSignature);
        device.WriteBlock(0, 1, mbr);
    }

    /// <summary>Rewrite the SectorCount field of slot <paramref name="index"/>, leaving systemId / startSector untouched.</summary>
    public static void ResizePartition(IBlockDevice device, int index, uint newSectorCount)
    {
        if ((uint)index >= 4)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "MBR primary partition index must be 0..3.");
        }

        Span<byte> mbr = new byte[device.BlockSize];
        device.ReadBlock(0, 1, mbr);

        int offset = PartitionTableOffset + index * PartitionEntrySize;
        if (mbr[offset + 4] == 0)
        {
            throw new InvalidOperationException("Cannot resize an empty MBR slot.");
        }

        BitConverter.TryWriteBytes(mbr.Slice(offset + 12, 4), newSectorCount);
        device.WriteBlock(0, 1, mbr);
    }

    /// <summary>Rewrite the StartSector field of slot <paramref name="index"/>, leaving systemId / sectorCount untouched. Table-level only — does not relocate data.</summary>
    public static void MovePartition(IBlockDevice device, int index, uint newStartSector)
    {
        if ((uint)index >= 4)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "MBR primary partition index must be 0..3.");
        }

        Span<byte> mbr = new byte[device.BlockSize];
        device.ReadBlock(0, 1, mbr);

        int offset = PartitionTableOffset + index * PartitionEntrySize;
        if (mbr[offset + 4] == 0)
        {
            throw new InvalidOperationException("Cannot move an empty MBR slot.");
        }

        BitConverter.TryWriteBytes(mbr.Slice(offset + 8, 4), newStartSector);
        device.WriteBlock(0, 1, mbr);
    }
}
