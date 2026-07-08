// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.HAL.Interfaces.Devices;

namespace Cosmos.Kernel.System.Storage;

/// <summary>
/// High-level partition lifecycle (create / delete / resize / move) on top
/// of <see cref="Mbr"/> and <see cref="Gpt"/>. Auto-detects the table type
/// per call. <see cref="MoveWithData"/> physically copies sectors before
/// rewriting the table; the other operations are pure table edits.
/// </summary>
public static class PartitionManager
{
    /// <summary>Identifies a partition by its absolute LBA range on the host disk.</summary>
    public readonly struct PartitionLocation
    {
        public ulong StartSector { get; }
        public ulong SectorCount { get; }

        public PartitionLocation(ulong startSector, ulong sectorCount)
        {
            StartSector = startSector;
            SectorCount = sectorCount;
        }
    }

    /// <summary>
    /// Add a partition. On a GPT disk uses <paramref name="gptType"/>; on an
    /// MBR disk picks the first free primary slot and uses
    /// <paramref name="mbrSystemId"/>. Returns false if no table is present
    /// or no slot is free.
    /// </summary>
    public static bool Create(
        IBlockDevice device,
        ulong startSector,
        ulong sectorCount,
        byte mbrSystemId,
        Guid gptType)
    {
        if (sectorCount == 0)
        {
            return false;
        }
        if (startSector + sectorCount > device.BlockCount)
        {
            return false;
        }

        if (Gpt.IsGpt(device))
        {
            return Gpt.AddPartition(device, startSector, sectorCount, gptType);
        }

        if (!Mbr.IsMbr(device))
        {
            return false;
        }

        int freeSlot = FindFreeMbrSlot(device);
        if (freeSlot < 0)
        {
            return false;
        }
        if (startSector > 0xFFFFFFFFUL || sectorCount > 0xFFFFFFFFUL)
        {
            return false;
        }

        Mbr.WritePartition(device, freeSlot, mbrSystemId, (uint)startSector, (uint)sectorCount);
        return true;
    }

    /// <summary>
    /// Add a logical partition to the disk's extended partition. Returns the
    /// new partition's absolute start LBA, or 0 on failure (no extended
    /// partition, no room left, or the disk is GPT).
    /// </summary>
    public static ulong CreateLogical(IBlockDevice device, byte systemId, ulong sectorCount)
    {
        if (Gpt.IsGpt(device))
        {
            return 0;
        }
        if (!Mbr.IsMbr(device))
        {
            return 0;
        }
        if (!Mbr.TryGetExtendedPartition(device, out ulong extStart, out ulong extCount))
        {
            return 0;
        }
        return Ebr.AddLogical(device, extStart, extCount, systemId, sectorCount);
    }

    /// <summary>Delete the partition occupying <paramref name="location"/>.</summary>
    public static bool Delete(IBlockDevice device, PartitionLocation location)
    {
        if (Gpt.IsGpt(device))
        {
            int gptIndex = FindGptIndex(device, location);
            if (gptIndex < 0)
            {
                return false;
            }
            return Gpt.RemovePartition(device, gptIndex);
        }

        if (!Mbr.IsMbr(device))
        {
            return false;
        }

        if (TryFindLogical(device, location, out ulong extStart, out int logicalIndex))
        {
            return Ebr.RemoveLogical(device, extStart, logicalIndex);
        }

        int slot = FindMbrSlot(device, location);
        if (slot < 0)
        {
            return false;
        }
        Mbr.DeletePartition(device, slot);
        return true;
    }

    /// <summary>Resize the partition at <paramref name="location"/> to <paramref name="newSectorCount"/>. Table-only; does not adjust the filesystem inside.</summary>
    public static bool Resize(IBlockDevice device, PartitionLocation location, ulong newSectorCount)
    {
        if (newSectorCount == 0)
        {
            return false;
        }
        if (location.StartSector + newSectorCount > device.BlockCount)
        {
            return false;
        }

        if (Gpt.IsGpt(device))
        {
            int gptIndex = FindGptIndex(device, location);
            if (gptIndex < 0)
            {
                return false;
            }
            return Gpt.ResizePartition(device, gptIndex, newSectorCount);
        }

        if (!Mbr.IsMbr(device))
        {
            return false;
        }

        if (TryFindLogical(device, location, out ulong extStart, out int logicalIndex))
        {
            return Ebr.ResizeLogical(device, extStart, logicalIndex, newSectorCount);
        }

        int slot = FindMbrSlot(device, location);
        if (slot < 0)
        {
            return false;
        }
        if (newSectorCount > 0xFFFFFFFFUL)
        {
            return false;
        }
        Mbr.ResizePartition(device, slot, (uint)newSectorCount);
        return true;
    }

    /// <summary>
    /// Physically relocate the partition at <paramref name="location"/> to
    /// start at <paramref name="newStartSector"/>: copies all sectors first,
    /// then rewrites the table entry. Direction-aware to handle overlap.
    /// </summary>
    public static bool MoveWithData(IBlockDevice device, PartitionLocation location, ulong newStartSector)
    {
        if (location.SectorCount == 0)
        {
            return false;
        }
        if (newStartSector + location.SectorCount > device.BlockCount)
        {
            return false;
        }
        if (newStartSector == location.StartSector)
        {
            return true;
        }

        CopySectors(device, location.StartSector, newStartSector, location.SectorCount);

        if (Gpt.IsGpt(device))
        {
            int gptIndex = FindGptIndex(device, location);
            if (gptIndex < 0)
            {
                return false;
            }
            return Gpt.MovePartition(device, gptIndex, newStartSector);
        }

        if (!Mbr.IsMbr(device))
        {
            return false;
        }

        if (TryFindLogical(device, location, out ulong extStart, out int logicalIndex))
        {
            return Ebr.MoveLogical(device, extStart, logicalIndex, newStartSector);
        }

        int slot = FindMbrSlot(device, location);
        if (slot < 0)
        {
            return false;
        }
        if (newStartSector > 0xFFFFFFFFUL)
        {
            return false;
        }
        Mbr.MovePartition(device, slot, (uint)newStartSector);
        return true;
    }

    private static int FindFreeMbrSlot(IBlockDevice device)
    {
        Span<byte> mbr = new byte[device.BlockSize];
        device.ReadBlock(0, 1, mbr);
        for (int i = 0; i < 4; i++)
        {
            int offset = 446 + i * 16;
            if (mbr[offset + 4] == 0)
            {
                return i;
            }
        }
        return -1;
    }

    private static int FindMbrSlot(IBlockDevice device, PartitionLocation location)
    {
        Span<byte> mbr = new byte[device.BlockSize];
        device.ReadBlock(0, 1, mbr);
        for (int i = 0; i < 4; i++)
        {
            int offset = 446 + i * 16;
            byte systemId = mbr[offset + 4];
            if (systemId == 0)
            {
                continue;
            }
            ulong start = BitConverter.ToUInt32(mbr.Slice(offset + 8, 4));
            ulong count = BitConverter.ToUInt32(mbr.Slice(offset + 12, 4));
            if (start == location.StartSector && count == location.SectorCount)
            {
                return i;
            }
        }
        return -1;
    }

    private static bool TryFindLogical(IBlockDevice device, PartitionLocation location, out ulong extendedStartLba, out int logicalIndex)
    {
        extendedStartLba = 0;
        logicalIndex = -1;

        if (!Mbr.TryGetExtendedPartition(device, out ulong extStart, out ulong extCount))
        {
            return false;
        }
        if (location.StartSector < extStart || location.StartSector + location.SectorCount > extStart + extCount)
        {
            return false;
        }

        List<Mbr.PartitionEntry> logicals = Ebr.Parse(device, extStart);
        for (int i = 0; i < logicals.Count; i++)
        {
            if (logicals[i].StartSector == location.StartSector && logicals[i].SectorCount == location.SectorCount)
            {
                extendedStartLba = extStart;
                logicalIndex = i;
                return true;
            }
        }

        return false;
    }

    private static int FindGptIndex(IBlockDevice device, PartitionLocation location)
    {
        List<Gpt.PartitionEntry> entries = Gpt.Parse(device);
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].StartSector == location.StartSector && entries[i].SectorCount == location.SectorCount)
            {
                return i;
            }
        }
        return -1;
    }

    private static void CopySectors(IBlockDevice device, ulong source, ulong destination, ulong count)
    {
        const int BatchBlocks = 128;
        ulong blockSize = device.BlockSize;
        Span<byte> buffer = new byte[(int)blockSize * BatchBlocks];

        bool overlapsForward = destination > source && destination < source + count;
        if (overlapsForward)
        {
            ulong remaining = count;
            while (remaining > 0)
            {
                ulong batch = remaining < BatchBlocks ? remaining : BatchBlocks;
                ulong tailOffset = remaining - batch;
                Span<byte> slice = buffer.Slice(0, (int)(batch * blockSize));
                device.ReadBlock(source + tailOffset, batch, slice);
                device.WriteBlock(destination + tailOffset, batch, slice);
                remaining -= batch;
            }
        }
        else
        {
            ulong copied = 0;
            while (copied < count)
            {
                ulong batch = count - copied < BatchBlocks ? count - copied : BatchBlocks;
                Span<byte> slice = buffer.Slice(0, (int)(batch * blockSize));
                device.ReadBlock(source + copied, batch, slice);
                device.WriteBlock(destination + copied, batch, slice);
                copied += batch;
            }
        }
    }
}
