// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.HAL.Interfaces.Devices;

namespace Cosmos.Kernel.System.Filesystems.Fat;

/// <summary>
/// FAT12 / FAT16 / FAT32 table accessor and chain manager. All entry
/// reads and writes go through the underlying <see cref="IBlockDevice"/>
/// using <see cref="FatBootSector"/> geometry.
/// </summary>
public sealed class FatTable
{
    public const uint FreeCluster = 0x00000000;
    public const uint Fat32EndOfChain = 0x0FFFFFF8;
    public const uint Fat32BadCluster = 0x0FFFFFF7;

    private readonly IBlockDevice _device;
    private readonly FatBootSector _boot;
    private uint _nextFreeHint = 2;

    public FatTable(IBlockDevice device, FatBootSector boot)
    {
        _device = device;
        _boot = boot;
    }

    public uint Get(uint cluster)
    {
        return _boot.Type switch
        {
            FatType.Fat32 => GetFat32(cluster),
            FatType.Fat16 => GetFat16(cluster),
            FatType.Fat12 => GetFat12(cluster),
            _ => 0,
        };
    }

    public void Set(uint cluster, uint value)
    {
        switch (_boot.Type)
        {
            case FatType.Fat32:
                SetFat32(cluster, value);
                break;
            case FatType.Fat16:
                SetFat16(cluster, value);
                break;
            case FatType.Fat12:
                SetFat12(cluster, value);
                break;
        }

        if (value == FreeCluster && cluster >= 2 && cluster < _nextFreeHint)
        {
            _nextFreeHint = cluster;
        }
    }

    public bool IsEndOfChain(uint entry)
    {
        return _boot.Type switch
        {
            FatType.Fat32 => entry >= 0x0FFFFFF8,
            FatType.Fat16 => entry >= 0xFFF8,
            FatType.Fat12 => entry >= 0x0FF8,
            _ => true,
        };
    }

    public bool IsBadCluster(uint entry)
    {
        return _boot.Type switch
        {
            FatType.Fat32 => entry == 0x0FFFFFF7,
            FatType.Fat16 => entry == 0xFFF7,
            FatType.Fat12 => entry == 0x0FF7,
            _ => false,
        };
    }

    public uint EndOfChainMarker()
    {
        return _boot.Type switch
        {
            FatType.Fat32 => 0x0FFFFFFF,
            FatType.Fat16 => 0xFFFF,
            FatType.Fat12 => 0x0FFF,
            _ => 0,
        };
    }

    /// <summary>
    /// Walk the chain starting at <paramref name="firstCluster"/>. Stops on
    /// EOC, free, or bad-cluster markers, and bails out if the chain
    /// exceeds the volume's cluster count to defend against loops.
    /// </summary>
    public List<uint> GetChain(uint firstCluster)
    {
        List<uint> chain = new();
        if (firstCluster < 2)
        {
            return chain;
        }

        uint current = firstCluster;
        uint guard = _boot.ClusterCount + 2;

        while (current >= 2 && !IsEndOfChain(current) && !IsBadCluster(current))
        {
            chain.Add(current);
            uint next = Get(current);
            if (next == current || chain.Count > guard)
            {
                break;
            }
            current = next;
        }

        return chain;
    }

    /// <summary>Allocate a chain of <paramref name="count"/> contiguous-by-FAT clusters; returns first cluster, or 0 on failure.</summary>
    public uint AllocateChain(uint count)
    {
        if (count == 0)
        {
            return 0;
        }

        uint first = 0;
        uint previous = 0;

        for (uint i = 0; i < count; i++)
        {
            uint cluster = FindFree();
            if (cluster == 0)
            {
                if (first != 0)
                {
                    Free(first);
                }
                return 0;
            }

            // Reserve immediately so the next FindFree skips it.
            Set(cluster, EndOfChainMarker());

            if (first == 0)
            {
                first = cluster;
            }

            if (previous != 0)
            {
                Set(previous, cluster);
            }

            previous = cluster;
        }

        return first;
    }

    /// <summary>Append <paramref name="count"/> clusters to the chain whose tail is <paramref name="lastCluster"/>; returns new tail or 0 on failure.</summary>
    public uint ExtendChain(uint lastCluster, uint count)
    {
        uint added = AllocateChain(count);
        if (added == 0)
        {
            return 0;
        }

        Set(lastCluster, added);
        uint tail = added;
        while (true)
        {
            uint next = Get(tail);
            if (IsEndOfChain(next))
            {
                return tail;
            }
            tail = next;
        }
    }

    /// <summary>Mark every cluster in the chain rooted at <paramref name="firstCluster"/> as free.</summary>
    public void Free(uint firstCluster)
    {
        List<uint> chain = GetChain(firstCluster);
        for (int i = 0; i < chain.Count; i++)
        {
            Set(chain[i], FreeCluster);
        }
    }

    public uint FindFree()
    {
        uint upper = _boot.ClusterCount + 2;
        uint start = _nextFreeHint < 2 ? 2u : _nextFreeHint;

        for (uint i = start; i < upper; i++)
        {
            if (Get(i) == FreeCluster)
            {
                _nextFreeHint = i + 1;
                return i;
            }
        }

        // Wrap and search from the beginning in case earlier clusters were freed.
        for (uint i = 2; i < start; i++)
        {
            if (Get(i) == FreeCluster)
            {
                _nextFreeHint = i + 1;
                return i;
            }
        }

        return 0;
    }

    public uint CountFree()
    {
        // Per-entry reads through Get() re-read the same sector for every
        // adjacent cluster, which on FAT32 means 128× the necessary I/O.
        // Sweep the FAT one sector at a time instead.
        if (_boot.Type == FatType.Fat12)
        {
            uint count = 0;
            for (uint i = 2; i < _boot.ClusterCount + 2; i++)
            {
                if (Get(i) == FreeCluster)
                {
                    count++;
                }
            }
            return count;
        }

        uint entrySize = _boot.Type == FatType.Fat32 ? 4u : 2u;
        uint entriesPerSector = _boot.BytesPerSector / entrySize;
        uint freeCount = 0;
        Span<byte> buffer = new byte[_boot.BytesPerSector];

        for (uint sectorIdx = 0; sectorIdx < _boot.FatSectorCount; sectorIdx++)
        {
            _device.ReadBlock(_boot.FatStartLba + sectorIdx, 1, buffer);
            for (uint j = 0; j < entriesPerSector; j++)
            {
                uint cluster = sectorIdx * entriesPerSector + j;
                if (cluster < 2 || cluster >= _boot.ClusterCount + 2)
                {
                    continue;
                }

                uint entry = entrySize == 4
                    ? BitConverter.ToUInt32(buffer.Slice((int)(j * 4), 4)) & 0x0FFFFFFFu
                    : BitConverter.ToUInt16(buffer.Slice((int)(j * 2), 2));
                if (entry == FreeCluster)
                {
                    freeCount++;
                }
            }
        }
        return freeCount;
    }

    private uint GetFat32(uint cluster)
    {
        uint fatOffset = cluster * 4;
        uint sectorNumber = _boot.FatStartLba + fatOffset / _boot.BytesPerSector;
        uint entryOffset = fatOffset % _boot.BytesPerSector;

        Span<byte> buffer = new byte[_boot.BytesPerSector];
        _device.ReadBlock(sectorNumber, 1, buffer);

        return BitConverter.ToUInt32(buffer.Slice((int)entryOffset, 4)) & 0x0FFFFFFF;
    }

    private void SetFat32(uint cluster, uint value)
    {
        uint fatOffset = cluster * 4;
        uint sectorNumber = _boot.FatStartLba + fatOffset / _boot.BytesPerSector;
        uint entryOffset = fatOffset % _boot.BytesPerSector;

        Span<byte> buffer = new byte[_boot.BytesPerSector];
        _device.ReadBlock(sectorNumber, 1, buffer);

        uint existing = BitConverter.ToUInt32(buffer.Slice((int)entryOffset, 4));
        uint merged = (existing & 0xF0000000u) | (value & 0x0FFFFFFFu);
        BitConverter.TryWriteBytes(buffer.Slice((int)entryOffset, 4), merged);

        for (uint i = 0; i < _boot.NumberOfFats; i++)
        {
            _device.WriteBlock(sectorNumber + i * _boot.FatSectorCount, 1, buffer);
        }
    }

    private uint GetFat16(uint cluster)
    {
        uint fatOffset = cluster * 2;
        uint sectorNumber = _boot.FatStartLba + fatOffset / _boot.BytesPerSector;
        uint entryOffset = fatOffset % _boot.BytesPerSector;

        Span<byte> buffer = new byte[_boot.BytesPerSector];
        _device.ReadBlock(sectorNumber, 1, buffer);

        return BitConverter.ToUInt16(buffer.Slice((int)entryOffset, 2));
    }

    private void SetFat16(uint cluster, uint value)
    {
        uint fatOffset = cluster * 2;
        uint sectorNumber = _boot.FatStartLba + fatOffset / _boot.BytesPerSector;
        uint entryOffset = fatOffset % _boot.BytesPerSector;

        Span<byte> buffer = new byte[_boot.BytesPerSector];
        _device.ReadBlock(sectorNumber, 1, buffer);

        BitConverter.TryWriteBytes(buffer.Slice((int)entryOffset, 2), (ushort)value);

        for (uint i = 0; i < _boot.NumberOfFats; i++)
        {
            _device.WriteBlock(sectorNumber + i * _boot.FatSectorCount, 1, buffer);
        }
    }

    private uint GetFat12(uint cluster)
    {
        uint fatOffset = cluster + cluster / 2;
        uint sectorNumber = _boot.FatStartLba + fatOffset / _boot.BytesPerSector;
        uint entryOffset = fatOffset % _boot.BytesPerSector;

        Span<byte> buffer = new byte[_boot.BytesPerSector];
        _device.ReadBlock(sectorNumber, 1, buffer);

        ushort word;
        if (entryOffset == _boot.BytesPerSector - 1)
        {
            Span<byte> next = new byte[_boot.BytesPerSector];
            _device.ReadBlock(sectorNumber + 1, 1, next);
            word = (ushort)(buffer[(int)entryOffset] | (next[0] << 8));
        }
        else
        {
            word = BitConverter.ToUInt16(buffer.Slice((int)entryOffset, 2));
        }

        return (cluster & 1) != 0 ? (uint)(word >> 4) : (uint)(word & 0x0FFFu);
    }

    private void SetFat12(uint cluster, uint value)
    {
        uint fatOffset = cluster + cluster / 2;
        uint sectorNumber = _boot.FatStartLba + fatOffset / _boot.BytesPerSector;
        uint entryOffset = fatOffset % _boot.BytesPerSector;
        bool spans = entryOffset == _boot.BytesPerSector - 1;

        Span<byte> buffer = new byte[_boot.BytesPerSector];
        _device.ReadBlock(sectorNumber, 1, buffer);

        Span<byte> next = Span<byte>.Empty;
        if (spans)
        {
            next = new byte[_boot.BytesPerSector];
            _device.ReadBlock(sectorNumber + 1, 1, next);
        }

        byte low = buffer[(int)entryOffset];
        byte high = spans ? next[0] : buffer[(int)entryOffset + 1];
        ushort word = (ushort)(low | (high << 8));

        if ((cluster & 1) != 0)
        {
            word = (ushort)(((uint)word & 0x000Fu) | ((value & 0x0FFFu) << 4));
        }
        else
        {
            word = (ushort)(((uint)word & 0xF000u) | (value & 0x0FFFu));
        }

        buffer[(int)entryOffset] = (byte)(word & 0xFF);
        if (spans)
        {
            next[0] = (byte)(word >> 8);
        }
        else
        {
            buffer[(int)entryOffset + 1] = (byte)(word >> 8);
        }

        for (uint i = 0; i < _boot.NumberOfFats; i++)
        {
            ulong fatBase = sectorNumber + i * _boot.FatSectorCount;
            _device.WriteBlock(fatBase, 1, buffer);
            if (spans)
            {
                _device.WriteBlock(fatBase + 1, 1, next);
            }
        }
    }
}
