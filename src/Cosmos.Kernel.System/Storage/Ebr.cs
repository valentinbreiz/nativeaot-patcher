// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.HAL.Interfaces.Devices;

namespace Cosmos.Kernel.System.Storage;

/// <summary>
/// EBR (Extended Boot Record) chain parser and editor. An extended partition
/// contains a singly-linked list of EBR sectors; each EBR holds one logical
/// partition entry plus an optional pointer to the next EBR. Convention used:
/// <list type="bullet">
///   <item><description>Logical-entry start LBA is relative to the EBR sector that holds it.</description></item>
///   <item><description>Next-EBR pointer is relative to the extended partition's start LBA (the standard convention).</description></item>
///   <item><description>The first EBR always sits at the extended partition's start LBA.</description></item>
///   <item><description>Each subsequent EBR is placed immediately after the prior logical's data.</description></item>
/// </list>
/// </summary>
public static class Ebr
{
    private const ushort EbrSignature = 0xAA55;
    private const int PartitionTableOffset = 446;
    private const int LogicalEntryOffset = PartitionTableOffset;
    private const int NextEbrEntryOffset = PartitionTableOffset + 16;
    private const int MaxChainLength = 128;

    private struct ChainNode
    {
        public ulong EbrLba;
        public byte LogicalSystemId;
        public uint LogicalRelativeStart;
        public uint LogicalSectorCount;
        public uint NextRelative;
    }

    /// <summary>
    /// Walk the EBR chain rooted at <paramref name="extendedStartLba"/> and
    /// return one entry per logical partition. <c>StartSector</c> values are
    /// absolute LBAs on <paramref name="device"/>.
    /// </summary>
    public static List<Mbr.PartitionEntry> Parse(IBlockDevice device, ulong extendedStartLba)
    {
        List<Mbr.PartitionEntry> logicals = new();
        if (device == null)
        {
            return logicals;
        }

        List<ChainNode> chain = WalkChain(device, extendedStartLba);
        for (int i = 0; i < chain.Count; i++)
        {
            ChainNode node = chain[i];
            if (node.LogicalSystemId == 0)
            {
                continue;
            }
            logicals.Add(new Mbr.PartitionEntry(
                node.LogicalSystemId,
                node.EbrLba + node.LogicalRelativeStart,
                node.LogicalSectorCount));
        }
        return logicals;
    }

    /// <summary>
    /// Append a logical partition to the chain. The new EBR is placed right
    /// after the prior logical's data, and the new logical's data area
    /// follows the new EBR (one sector for the EBR, then <paramref name="sectorCount"/> sectors).
    /// Returns the new logical's absolute start LBA, or 0 on failure.
    /// </summary>
    public static ulong AddLogical(
        IBlockDevice device,
        ulong extendedStartLba,
        ulong extendedSectorCount,
        byte systemId,
        ulong sectorCount)
    {
        if (sectorCount == 0 || systemId == 0 || systemId == 0x05 || systemId == 0x0F || systemId == 0x85)
        {
            return 0;
        }

        ulong extendedEnd = extendedStartLba + extendedSectorCount;
        List<ChainNode> chain = WalkChain(device, extendedStartLba);

        ulong newEbrLba;
        if (chain.Count == 0)
        {
            newEbrLba = extendedStartLba;
        }
        else
        {
            ChainNode tail = chain[^1];
            newEbrLba = tail.EbrLba + tail.LogicalRelativeStart + tail.LogicalSectorCount;
        }

        if (newEbrLba + 1 + sectorCount > extendedEnd)
        {
            return 0;
        }

        WriteEbrSector(device, newEbrLba, systemId, relativeStart: 1, sectorCount: (uint)sectorCount, nextRelative: 0);

        if (chain.Count > 0)
        {
            ChainNode tail = chain[^1];
            uint newNextRelative = (uint)(newEbrLba - extendedStartLba);
            WriteEbrSector(
                device,
                tail.EbrLba,
                tail.LogicalSystemId,
                tail.LogicalRelativeStart,
                tail.LogicalSectorCount,
                newNextRelative);
        }

        return newEbrLba + 1;
    }

    /// <summary>
    /// Remove the <paramref name="logicalIndex"/>-th logical partition from
    /// the chain (0-based, in chain order).
    /// </summary>
    public static bool RemoveLogical(IBlockDevice device, ulong extendedStartLba, int logicalIndex)
    {
        List<ChainNode> chain = WalkChain(device, extendedStartLba);
        if (logicalIndex < 0 || logicalIndex >= chain.Count)
        {
            return false;
        }

        if (logicalIndex == 0)
        {
            if (chain.Count == 1)
            {
                Span<byte> wipe = new byte[device.BlockSize];
                device.WriteBlock(extendedStartLba, 1, wipe);
                return true;
            }

            // Promote node[1] into the fixed first EBR slot at extendedStartLba.
            // LogicalRelativeStart is relative to the EBR sector holding the
            // entry, so it must be rebased from successor.EbrLba to the first
            // EBR; NextRelative is already extended-relative and stays as-is.
            ChainNode successor = chain[1];
            uint promotedRelativeStart =
                (uint)(successor.EbrLba + successor.LogicalRelativeStart - extendedStartLba);
            WriteEbrSector(
                device,
                extendedStartLba,
                successor.LogicalSystemId,
                promotedRelativeStart,
                successor.LogicalSectorCount,
                successor.NextRelative);
            return true;
        }

        // Bypass: predecessor's next pointer skips the deleted node.
        ChainNode predecessor = chain[logicalIndex - 1];
        ChainNode target = chain[logicalIndex];
        WriteEbrSector(
            device,
            predecessor.EbrLba,
            predecessor.LogicalSystemId,
            predecessor.LogicalRelativeStart,
            predecessor.LogicalSectorCount,
            target.NextRelative);
        return true;
    }

    /// <summary>Rewrite the SectorCount of the <paramref name="logicalIndex"/>-th logical partition.</summary>
    public static bool ResizeLogical(
        IBlockDevice device,
        ulong extendedStartLba,
        int logicalIndex,
        ulong newSectorCount)
    {
        if (newSectorCount == 0 || newSectorCount > 0xFFFFFFFFUL)
        {
            return false;
        }

        List<ChainNode> chain = WalkChain(device, extendedStartLba);
        if (logicalIndex < 0 || logicalIndex >= chain.Count)
        {
            return false;
        }

        ChainNode node = chain[logicalIndex];
        ulong absoluteEnd = node.EbrLba + node.LogicalRelativeStart + newSectorCount;
        ulong upperBound = logicalIndex + 1 < chain.Count
            ? chain[logicalIndex + 1].EbrLba
            : extendedStartLba + ResolveExtendedCount(device, extendedStartLba);
        if (absoluteEnd > upperBound)
        {
            return false;
        }

        WriteEbrSector(
            device,
            node.EbrLba,
            node.LogicalSystemId,
            node.LogicalRelativeStart,
            (uint)newSectorCount,
            node.NextRelative);
        return true;
    }

    /// <summary>
    /// Rewrite the start LBA of the <paramref name="logicalIndex"/>-th logical
    /// partition. The hosting EBR sector stays put; only the relative offset
    /// inside the EBR changes. Caller is responsible for making sure data
    /// at the new range is what's expected (use
    /// <see cref="PartitionManager.MoveWithData"/> for a data-copying move).
    /// </summary>
    public static bool MoveLogical(
        IBlockDevice device,
        ulong extendedStartLba,
        int logicalIndex,
        ulong newAbsoluteStart)
    {
        List<ChainNode> chain = WalkChain(device, extendedStartLba);
        if (logicalIndex < 0 || logicalIndex >= chain.Count)
        {
            return false;
        }

        ChainNode node = chain[logicalIndex];
        if (newAbsoluteStart <= node.EbrLba)
        {
            return false;
        }

        ulong newRelative = newAbsoluteStart - node.EbrLba;
        if (newRelative > 0xFFFFFFFFUL)
        {
            return false;
        }

        ulong newAbsoluteEnd = newAbsoluteStart + node.LogicalSectorCount;
        ulong upperBound = logicalIndex + 1 < chain.Count
            ? chain[logicalIndex + 1].EbrLba
            : extendedStartLba + ResolveExtendedCount(device, extendedStartLba);
        if (newAbsoluteEnd > upperBound)
        {
            return false;
        }

        WriteEbrSector(
            device,
            node.EbrLba,
            node.LogicalSystemId,
            (uint)newRelative,
            node.LogicalSectorCount,
            node.NextRelative);
        return true;
    }

    private static List<ChainNode> WalkChain(IBlockDevice device, ulong extendedStartLba)
    {
        List<ChainNode> nodes = new();
        if (device == null)
        {
            return nodes;
        }

        ulong currentEbrLba = extendedStartLba;
        int hops = 0;

        while (hops < MaxChainLength)
        {
            Span<byte> sector = new byte[device.BlockSize];
            device.ReadBlock(currentEbrLba, 1, sector);

            if (BitConverter.ToUInt16(sector.Slice(510, 2)) != EbrSignature)
            {
                break;
            }

            byte logicalSystemId = sector[LogicalEntryOffset + 4];
            uint logicalRelativeStart = BitConverter.ToUInt32(sector.Slice(LogicalEntryOffset + 8, 4));
            uint logicalSectorCount = BitConverter.ToUInt32(sector.Slice(LogicalEntryOffset + 12, 4));

            byte nextSystemId = sector[NextEbrEntryOffset + 4];
            uint nextRelative = (nextSystemId == 0x05 || nextSystemId == 0x0F || nextSystemId == 0x85)
                ? BitConverter.ToUInt32(sector.Slice(NextEbrEntryOffset + 8, 4))
                : 0u;

            if (logicalSystemId != 0)
            {
                nodes.Add(new ChainNode
                {
                    EbrLba = currentEbrLba,
                    LogicalSystemId = logicalSystemId,
                    LogicalRelativeStart = logicalRelativeStart,
                    LogicalSectorCount = logicalSectorCount,
                    NextRelative = nextRelative,
                });
            }

            if (nextRelative == 0)
            {
                break;
            }

            currentEbrLba = extendedStartLba + nextRelative;
            hops++;
        }

        return nodes;
    }

    private static void WriteEbrSector(
        IBlockDevice device,
        ulong ebrLba,
        byte logicalSystemId,
        uint relativeStart,
        uint sectorCount,
        uint nextRelative)
    {
        Span<byte> sector = new byte[device.BlockSize];

        sector[LogicalEntryOffset + 4] = logicalSystemId;
        BitConverter.TryWriteBytes(sector.Slice(LogicalEntryOffset + 8, 4), relativeStart);
        BitConverter.TryWriteBytes(sector.Slice(LogicalEntryOffset + 12, 4), sectorCount);

        if (nextRelative != 0)
        {
            sector[NextEbrEntryOffset + 4] = 0x05;
            BitConverter.TryWriteBytes(sector.Slice(NextEbrEntryOffset + 8, 4), nextRelative);
        }

        sector[510] = 0x55;
        sector[511] = 0xAA;

        device.WriteBlock(ebrLba, 1, sector);
    }

    private static ulong ResolveExtendedCount(IBlockDevice device, ulong extendedStartLba)
    {
        if (Mbr.TryGetExtendedPartition(device, out ulong start, out ulong count) && start == extendedStartLba)
        {
            return count;
        }
        return device.BlockCount > extendedStartLba ? device.BlockCount - extendedStartLba : 0;
    }
}
