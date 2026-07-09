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
    /// <summary>Largest value a 32-bit MBR LBA / sector-count field can hold.</summary>
    private const ulong MbrMaxLbaValue = uint.MaxValue;

    /// <summary>Sectors copied per ReadBlock/WriteBlock batch in <see cref="CopySectors"/>.</summary>
    private const int BatchBlocks = 128;

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
        // Start 0 aliases the table sector on both formats; the MBR writer
        // throws for it, but this facade documents a false return.
        if (sectorCount == 0 || startSector == 0)
        {
            return false;
        }
        // Non-wrapping bound (cf. Gpt.AddPartition): the naive sum wraps
        // 2^64 for large inputs and slips past the check.
        if (startSector >= device.BlockCount || sectorCount > device.BlockCount - startSector)
        {
            return false;
        }
        // The low-level writers only validate device bounds; the facade is
        // where the free-space invariant lives.
        if (RangeIntersectsExistingPartition(device, startSector, sectorCount, exclude: null))
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
        if (startSector > MbrMaxLbaValue || sectorCount > MbrMaxLbaValue)
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
        // Non-wrapping bound: the naive sum wraps 2^64 for large counts.
        if (location.StartSector >= device.BlockCount
            || newSectorCount > device.BlockCount - location.StartSector)
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
        if (newSectorCount > MbrMaxLbaValue)
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
        // Non-wrapping bounds for both the source range (it drives raw
        // ReadBlock batches) and the destination: the naive sums wrap 2^64.
        if (location.StartSector >= device.BlockCount
            || location.SectorCount > device.BlockCount - location.StartSector)
        {
            return false;
        }
        if (newStartSector >= device.BlockCount
            || location.SectorCount > device.BlockCount - newStartSector)
        {
            return false;
        }
        if (newStartSector == location.StartSector)
        {
            return true;
        }
        // The destination must be free space (the source itself may
        // overlap it; CopySectors is direction-aware). With this checked
        // up front, a copy that later fails to re-table only ever landed
        // on unallocated sectors.
        if (RangeIntersectsExistingPartition(device, newStartSector, location.SectorCount, location))
        {
            return false;
        }

        // Resolve the table entry and its constraints BEFORE copying, so a
        // false return is side-effect free. The copy-then-retable order
        // stays (a crash between the two leaves the old table pointing at
        // intact data), and the Flush makes the copied data durable before
        // the table points at it.
        if (Gpt.IsGpt(device))
        {
            int gptIndex = FindGptIndex(device, location);
            if (gptIndex < 0)
            {
                return false;
            }
            CopySectors(device, location.StartSector, newStartSector, location.SectorCount);
            device.Flush();
            return Gpt.MovePartition(device, gptIndex, newStartSector);
        }

        if (!Mbr.IsMbr(device))
        {
            return false;
        }

        if (TryFindLogical(device, location, out ulong extStart, out int logicalIndex))
        {
            CopySectors(device, location.StartSector, newStartSector, location.SectorCount);
            device.Flush();
            return Ebr.MoveLogical(device, extStart, logicalIndex, newStartSector);
        }

        int slot = FindMbrSlot(device, location);
        if (slot < 0)
        {
            return false;
        }
        if (newStartSector > MbrMaxLbaValue)
        {
            return false;
        }
        CopySectors(device, location.StartSector, newStartSector, location.SectorCount);
        device.Flush();
        Mbr.MovePartition(device, slot, (uint)newStartSector);
        return true;
    }

    /// <summary>
    /// True when [<paramref name="startSector"/>, +<paramref name="sectorCount"/>)
    /// intersects a partition other than <paramref name="exclude"/>. On MBR
    /// disks, logical partitions are checked including the EBR sector
    /// preceding each logical's data plus the first EBR at the extended
    /// start; for non-logical ranges the whole extended container counts
    /// as occupied.
    /// </summary>
    private static bool RangeIntersectsExistingPartition(
        IBlockDevice device,
        ulong startSector,
        ulong sectorCount,
        PartitionLocation? exclude)
    {
        if (Gpt.IsGpt(device))
        {
            List<Gpt.PartitionEntry> entries = Gpt.Parse(device);
            for (int i = 0; i < entries.Count; i++)
            {
                if (IsExcluded(entries[i].StartSector, entries[i].SectorCount, exclude))
                {
                    continue;
                }
                if (Intersects(startSector, sectorCount, entries[i].StartSector, entries[i].SectorCount))
                {
                    return true;
                }
            }
            return false;
        }

        if (!Mbr.IsMbr(device))
        {
            return false;
        }

        List<Mbr.PartitionEntry> primaries = Mbr.Parse(device);
        for (int i = 0; i < primaries.Count; i++)
        {
            if (IsExcluded(primaries[i].StartSector, primaries[i].SectorCount, exclude))
            {
                continue;
            }
            if (Intersects(startSector, sectorCount, primaries[i].StartSector, primaries[i].SectorCount))
            {
                return true;
            }
        }

        if (!Mbr.TryGetExtendedPartition(device, out ulong extStart, out ulong extCount))
        {
            return false;
        }

        bool excludeIsLogical = exclude.HasValue
            && TryFindLogical(device, exclude.Value, out _, out _);
        if (!excludeIsLogical)
        {
            // Only logicals may live inside the container; any other range
            // treats the whole container (chain sectors included) as
            // occupied.
            return Intersects(startSector, sectorCount, extStart, extCount);
        }

        // A logical moving inside its container: the obstacles are the
        // other logicals (each with the EBR sector preceding its data)
        // and the chain's first EBR sector. The mover's own EBR stays put
        // and Ebr.MoveLogical already enforces newStart past it.
        List<Mbr.PartitionEntry> logicals = Ebr.Parse(device, extStart);
        for (int i = 0; i < logicals.Count; i++)
        {
            if (IsExcluded(logicals[i].StartSector, logicals[i].SectorCount, exclude))
            {
                continue;
            }
            if (Intersects(startSector, sectorCount, logicals[i].StartSector - 1, logicals[i].SectorCount + 1))
            {
                return true;
            }
        }
        return Intersects(startSector, sectorCount, extStart, 1);
    }

    /// <summary>Half-open interval intersection on absolute LBA ranges.</summary>
    private static bool Intersects(ulong aStart, ulong aCount, ulong bStart, ulong bCount)
    {
        return aStart < bStart + bCount && bStart < aStart + aCount;
    }

    /// <summary>True when the entry range is the one <paramref name="exclude"/> designates.</summary>
    private static bool IsExcluded(ulong startSector, ulong sectorCount, PartitionLocation? exclude)
    {
        return exclude.HasValue
            && exclude.Value.StartSector == startSector
            && exclude.Value.SectorCount == sectorCount;
    }

    private static int FindFreeMbrSlot(IBlockDevice device)
    {
        Span<byte> mbr = new byte[device.BlockSize];
        device.ReadBlock(0, 1, mbr);
        for (int i = 0; i < Mbr.MaxPartitions; i++)
        {
            int offset = Mbr.PartitionTableOffset + i * Mbr.PartitionEntrySize;
            if (mbr[offset + Mbr.EntrySystemIdOffset] == 0)
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
        for (int i = 0; i < Mbr.MaxPartitions; i++)
        {
            int offset = Mbr.PartitionTableOffset + i * Mbr.PartitionEntrySize;
            byte systemId = mbr[offset + Mbr.EntrySystemIdOffset];
            if (systemId == 0)
            {
                continue;
            }
            ulong start = BitConverter.ToUInt32(mbr.Slice(offset + Mbr.EntryStartLbaOffset, Mbr.LbaFieldSizeBytes));
            ulong count = BitConverter.ToUInt32(mbr.Slice(offset + Mbr.EntrySectorCountOffset, Mbr.LbaFieldSizeBytes));
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
        ulong blockSize = device.BlockSize;
        Span<byte> buffer = new byte[(int)blockSize * BatchBlocks];

        // Non-wrapping overlap test: source + count can wrap 2^64.
        bool overlapsForward = destination > source && destination - source < count;
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
