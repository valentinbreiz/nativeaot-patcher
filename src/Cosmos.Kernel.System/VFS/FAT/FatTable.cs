// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core.IO;

namespace Cosmos.Kernel.System.VFS.FAT;

/// <summary>
/// FAT table management class for reading/writing FAT entries.
/// </summary>
public class FatTable
{
    private readonly FatFileSystem _fileSystem;
    private readonly uint _fatSector;

    /// <summary>
    /// End of cluster chain marker.
    /// </summary>
    public const uint EndOfChain = 0x0FFFFFF8;

    /// <summary>
    /// Free cluster marker.
    /// </summary>
    public const uint FreeCluster = 0x00000000;

    /// <summary>
    /// Bad cluster marker.
    /// </summary>
    public const uint BadCluster = 0x0FFFFFF7;

    public FatTable(FatFileSystem fileSystem, uint fatSector)
    {
        _fileSystem = fileSystem;
        _fatSector = fatSector;
    }

    /// <summary>
    /// Gets the FAT entry for a cluster.
    /// </summary>
    public uint GetFatEntry(uint cluster)
    {
        switch (_fileSystem.FatTypeValue)
        {
            case FatFileSystem.FatType.Fat32:
                return GetFat32Entry(cluster);
            case FatFileSystem.FatType.Fat16:
                return GetFat16Entry(cluster);
            case FatFileSystem.FatType.Fat12:
                return GetFat12Entry(cluster);
            default:
                throw new NotSupportedException("Unknown FAT type");
        }
    }

    /// <summary>
    /// Sets the FAT entry for a cluster.
    /// </summary>
    public void SetFatEntry(uint cluster, uint value)
    {
        switch (_fileSystem.FatTypeValue)
        {
            case FatFileSystem.FatType.Fat32:
                SetFat32Entry(cluster, value);
                break;
            case FatFileSystem.FatType.Fat16:
                SetFat16Entry(cluster, value);
                break;
            case FatFileSystem.FatType.Fat12:
                SetFat12Entry(cluster, value);
                break;
            default:
                throw new NotSupportedException("Unknown FAT type");
        }
    }

    /// <summary>
    /// Checks if a cluster value indicates end of chain.
    /// </summary>
    public bool IsEndOfChain(uint cluster)
    {
        return _fileSystem.FatTypeValue switch
        {
            FatFileSystem.FatType.Fat32 => cluster >= 0x0FFFFFF8,
            FatFileSystem.FatType.Fat16 => cluster >= 0xFFF8,
            FatFileSystem.FatType.Fat12 => cluster >= 0x0FF8,
            _ => true
        };
    }

    /// <summary>
    /// Gets the cluster chain starting from a given cluster.
    /// </summary>
    public List<uint> GetClusterChain(uint startCluster)
    {
        var chain = new List<uint>();
        uint current = startCluster;

        while (!IsEndOfChain(current) && current != FreeCluster && current != BadCluster)
        {
            chain.Add(current);
            uint next = GetFatEntry(current);
            if (next == current)
            {
                Serial.WriteString("[FAT] WARNING: Self-referencing cluster detected\n");
                break;
            }
            current = next;

            // Safety check to prevent infinite loops
            if (chain.Count > _fileSystem.ClusterCount)
            {
                Serial.WriteString("[FAT] ERROR: Cluster chain too long\n");
                break;
            }
        }

        return chain;
    }

    /// <summary>
    /// Finds a free cluster.
    /// </summary>
    public uint FindFreeCluster()
    {
        for (uint i = 2; i < _fileSystem.ClusterCount + 2; i++)
        {
            if (GetFatEntry(i) == FreeCluster)
            {
                return i;
            }
        }
        return 0; // No free cluster found
    }

    /// <summary>
    /// Allocates a chain of clusters.
    /// </summary>
    public uint AllocateClusterChain(uint count)
    {
        if (count == 0) return 0;

        uint firstCluster = 0;
        uint previousCluster = 0;

        for (uint i = 0; i < count; i++)
        {
            uint cluster = FindFreeCluster();
            if (cluster == 0)
            {
                Serial.WriteString("[FAT] ERROR: No free clusters available\n");
                return 0;
            }

            if (firstCluster == 0)
            {
                firstCluster = cluster;
            }

            if (previousCluster != 0)
            {
                SetFatEntry(previousCluster, cluster);
            }

            previousCluster = cluster;
        }

        // Mark end of chain
        if (previousCluster != 0)
        {
            SetFatEntry(previousCluster, EndOfChain);
        }

        return firstCluster;
    }

    /// <summary>
    /// Frees a cluster chain.
    /// </summary>
    public void FreeClusterChain(uint startCluster)
    {
        var chain = GetClusterChain(startCluster);
        foreach (uint cluster in chain)
        {
            SetFatEntry(cluster, FreeCluster);
        }
    }

    #region FAT32 Implementation

    private uint GetFat32Entry(uint cluster)
    {
        uint fatOffset = cluster * 4;
        uint sectorNumber = _fatSector + (fatOffset / _fileSystem.BytesPerSector);
        uint entryOffset = fatOffset % _fileSystem.BytesPerSector;

        Span<byte> buffer = new byte[_fileSystem.BytesPerSector];
        _fileSystem.Partition.ReadBlock(sectorNumber, 1, buffer);

        return BitConverter.ToUInt32(buffer.Slice((int)entryOffset, 4)) & 0x0FFFFFFF;
    }

    private void SetFat32Entry(uint cluster, uint value)
    {
        uint fatOffset = cluster * 4;
        uint sectorNumber = _fatSector + (fatOffset / _fileSystem.BytesPerSector);
        uint entryOffset = fatOffset % _fileSystem.BytesPerSector;

        Span<byte> buffer = new byte[_fileSystem.BytesPerSector];
        _fileSystem.Partition.ReadBlock(sectorNumber, 1, buffer);

        // Preserve high 4 bits
        uint existing = BitConverter.ToUInt32(buffer.Slice((int)entryOffset, 4));
        value = (existing & 0xF0000000) | (value & 0x0FFFFFFF);
        BitConverter.TryWriteBytes(buffer.Slice((int)entryOffset, 4), value);

        // Write to all FAT copies
        for (uint i = 0; i < _fileSystem.NumberOfFATs; i++)
        {
            _fileSystem.Partition.WriteBlock(sectorNumber + i * _fileSystem.FatSectorCount, 1, buffer);
        }
    }

    #endregion

    #region FAT16 Implementation

    private uint GetFat16Entry(uint cluster)
    {
        uint fatOffset = cluster * 2;
        uint sectorNumber = _fatSector + (fatOffset / _fileSystem.BytesPerSector);
        uint entryOffset = fatOffset % _fileSystem.BytesPerSector;

        Span<byte> buffer = new byte[_fileSystem.BytesPerSector];
        _fileSystem.Partition.ReadBlock(sectorNumber, 1, buffer);

        return BitConverter.ToUInt16(buffer.Slice((int)entryOffset, 2));
    }

    private void SetFat16Entry(uint cluster, uint value)
    {
        uint fatOffset = cluster * 2;
        uint sectorNumber = _fatSector + (fatOffset / _fileSystem.BytesPerSector);
        uint entryOffset = fatOffset % _fileSystem.BytesPerSector;

        Span<byte> buffer = new byte[_fileSystem.BytesPerSector];
        _fileSystem.Partition.ReadBlock(sectorNumber, 1, buffer);

        BitConverter.TryWriteBytes(buffer.Slice((int)entryOffset, 2), (ushort)value);

        for (uint i = 0; i < _fileSystem.NumberOfFATs; i++)
        {
            _fileSystem.Partition.WriteBlock(sectorNumber + i * _fileSystem.FatSectorCount, 1, buffer);
        }
    }

    #endregion

    #region FAT12 Implementation

    private uint GetFat12Entry(uint cluster)
    {
        uint fatOffset = cluster + (cluster / 2);
        uint sectorNumber = _fatSector + (fatOffset / _fileSystem.BytesPerSector);
        uint entryOffset = fatOffset % _fileSystem.BytesPerSector;

        Span<byte> buffer = new byte[_fileSystem.BytesPerSector];
        _fileSystem.Partition.ReadBlock(sectorNumber, 1, buffer);

        ushort value;
        if (entryOffset == _fileSystem.BytesPerSector - 1)
        {
            // Entry spans sector boundary
            Span<byte> nextBuffer = new byte[_fileSystem.BytesPerSector];
            _fileSystem.Partition.ReadBlock(sectorNumber + 1, 1, nextBuffer);
            value = (ushort)(buffer[(int)entryOffset] | (nextBuffer[0] << 8));
        }
        else
        {
            value = BitConverter.ToUInt16(buffer.Slice((int)entryOffset, 2));
        }

        if ((cluster & 1) != 0)
        {
            return (uint)(value >> 4);
        }
        else
        {
            return (uint)(value & 0x0FFF);
        }
    }

    private void SetFat12Entry(uint cluster, uint value)
    {
        // FAT12 write is complex due to 12-bit entries spanning byte boundaries
        // For now, throw not implemented
        throw new NotImplementedException("FAT12 write not yet implemented");
    }

    #endregion
}
