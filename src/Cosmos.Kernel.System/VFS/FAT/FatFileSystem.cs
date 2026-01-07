// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.System.VFS.Interfaces;

namespace Cosmos.Kernel.System.VFS.FAT;

/// <summary>
/// FAT filesystem implementation supporting FAT12, FAT16, and FAT32.
/// </summary>
public class FatFileSystem : IFileSystem, IFileSystemFormatter
{
    /// <summary>
    /// FAT type enumeration.
    /// </summary>
    public enum FatType
    {
        Unknown,
        Fat12,
        Fat16,
        Fat32
    }

    // Boot Parameter Block (BPB) values
    public uint BytesPerSector { get; private set; }
    public uint SectorsPerCluster { get; private set; }
    public uint BytesPerCluster { get; private set; }
    public uint ReservedSectorCount { get; private set; }
    public uint NumberOfFATs { get; private set; }
    public uint RootEntryCount { get; private set; }
    public uint TotalSectorCount { get; private set; }
    public uint FatSectorCount { get; private set; }
    public uint RootCluster { get; private set; }
    public uint RootSector { get; private set; }
    public uint RootSectorCount { get; private set; }
    public uint DataSector { get; private set; }
    public uint DataSectorCount { get; private set; }
    public uint ClusterCount { get; private set; }
    public FatType FatTypeValue { get; private set; }

    private FatTable? _fat;

    public FatFileSystem(Partition partition, string root)
    {
        Partition = partition;
        RootPath = root;
        ReadBootSector();
    }

    /// <summary>
    /// Private constructor for formatting (no boot sector read).
    /// </summary>
    private FatFileSystem(Partition partition, string root, bool skipRead)
    {
        Partition = partition;
        RootPath = root;
        if (!skipRead)
        {
            ReadBootSector();
        }
    }

    /// <summary>
    /// Reads and parses the boot sector (BPB).
    /// </summary>
    private void ReadBootSector()
    {
        Serial.WriteString("[FAT] Reading boot sector...\n");

        Span<byte> bpb = new byte[512];
        Partition.ReadBlock(0, 1, bpb);

        // Check signature
        ushort sig = BitConverter.ToUInt16(bpb.Slice(510, 2));
        if (sig != 0xAA55)
        {
            Serial.WriteString("[FAT] ERROR: Invalid FAT signature\n");
            throw new Exception("FAT signature not found.");
        }

        // Parse BPB
        BytesPerSector = BitConverter.ToUInt16(bpb.Slice(11, 2));
        SectorsPerCluster = bpb[13];
        BytesPerCluster = BytesPerSector * SectorsPerCluster;
        ReservedSectorCount = BitConverter.ToUInt16(bpb.Slice(14, 2));
        NumberOfFATs = bpb[16];
        RootEntryCount = BitConverter.ToUInt16(bpb.Slice(17, 2));

        TotalSectorCount = BitConverter.ToUInt16(bpb.Slice(19, 2));
        if (TotalSectorCount == 0)
        {
            TotalSectorCount = BitConverter.ToUInt32(bpb.Slice(32, 4));
        }

        FatSectorCount = BitConverter.ToUInt16(bpb.Slice(22, 2));
        if (FatSectorCount == 0)
        {
            FatSectorCount = BitConverter.ToUInt32(bpb.Slice(36, 4));
        }

        DataSectorCount = TotalSectorCount - (ReservedSectorCount + NumberOfFATs * FatSectorCount + ReservedSectorCount);
        ClusterCount = DataSectorCount / SectorsPerCluster;

        // Determine FAT type based on cluster count
        if (ClusterCount < 4085)
        {
            FatTypeValue = FatType.Fat12;
        }
        else if (ClusterCount < 65525)
        {
            FatTypeValue = FatType.Fat16;
        }
        else
        {
            FatTypeValue = FatType.Fat32;
        }

        if (FatTypeValue == FatType.Fat32)
        {
            RootCluster = BitConverter.ToUInt32(bpb.Slice(44, 4));
        }
        else
        {
            RootSector = ReservedSectorCount + NumberOfFATs * FatSectorCount;
            RootSectorCount = (RootEntryCount * 32 + (BytesPerSector - 1)) / BytesPerSector;
        }

        DataSector = ReservedSectorCount + NumberOfFATs * FatSectorCount + RootSectorCount;

        // Initialize FAT table
        _fat = new FatTable(this, ReservedSectorCount);

        Serial.WriteString("[FAT] Type: ");
        Serial.WriteString(Type);
        Serial.WriteString("\n");
        Serial.WriteString("[FAT] BytesPerSector: ");
        Serial.WriteNumber(BytesPerSector);
        Serial.WriteString("\n");
        Serial.WriteString("[FAT] SectorsPerCluster: ");
        Serial.WriteNumber(SectorsPerCluster);
        Serial.WriteString("\n");
        Serial.WriteString("[FAT] ClusterCount: ");
        Serial.WriteNumber(ClusterCount);
        Serial.WriteString("\n");
    }

    /// <summary>
    /// Gets the FAT table.
    /// </summary>
    public FatTable? GetFat() => _fat;

    /// <summary>
    /// Creates a new byte array for reading a cluster.
    /// </summary>
    public byte[] NewClusterArray() => new byte[BytesPerCluster];

    /// <summary>
    /// Reads data from a cluster.
    /// </summary>
    public void ReadCluster(uint cluster, Span<byte> data)
    {
        if (FatTypeValue == FatType.Fat32)
        {
            ulong sector = DataSector + (ulong)(cluster - RootCluster) * SectorsPerCluster;
            Partition.ReadBlock(sector, SectorsPerCluster, data);
        }
        else
        {
            Partition.ReadBlock(cluster, RootSectorCount, data);
        }
    }

    /// <summary>
    /// Writes data to a cluster.
    /// </summary>
    public void WriteCluster(uint cluster, Span<byte> data)
    {
        if (FatTypeValue == FatType.Fat32)
        {
            ulong sector = DataSector + (ulong)(cluster - RootCluster) * SectorsPerCluster;
            Partition.WriteBlock(sector, SectorsPerCluster, data);
        }
        else
        {
            Partition.WriteBlock(cluster, RootSectorCount, data);
        }
    }

    #region IFileSystem Implementation

    public List<IDirectoryEntry> GetDirectoryListing(IDirectoryEntry directoryEntry)
    {
        var result = new List<IDirectoryEntry>();

        if (directoryEntry is not FatDirectoryEntry fatDir)
        {
            return result;
        }

        return fatDir.ReadDirectoryContents();
    }

    public IDirectoryEntry? Get(string path)
    {
        string localPath = path.Length > RootPath.Length ? path[RootPath.Length..] : "";
        if (string.IsNullOrEmpty(localPath) || localPath == "/" || localPath == "\\")
        {
            return GetRootDirectory();
        }

        string[] parts = localPath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        IDirectoryEntry? current = GetRootDirectory();

        foreach (string part in parts)
        {
            if (current == null) break;
            List<IDirectoryEntry> nodes = GetDirectoryListing(current);
            current = null;
            foreach (IDirectoryEntry node in nodes)
            {
                if (string.Equals(node.Name, part, StringComparison.OrdinalIgnoreCase))
                {
                    current = node;
                    break;
                }
            }
        }

        return current;
    }

    public IDirectoryEntry GetRootDirectory()
    {
        return new FatDirectoryEntry(this, null, RootPath, RootPath, (long)Size, RootCluster);
    }

    public IDirectoryEntry CreateDirectory(IDirectoryEntry directoryEntry, string aNewDirectory)
        => throw new NotImplementedException();

    public IDirectoryEntry CreateFile(IDirectoryEntry directoryEntry, string aNewFile)
        => throw new NotImplementedException();

    public void DeleteDirectory(IDirectoryEntry directoryEntry)
        => throw new NotImplementedException();

    public void DeleteFile(IDirectoryEntry directoryEntry)
        => throw new NotImplementedException();

    public Partition Partition { get; }
    public string RootPath { get; }

    public ulong Size => (ulong)TotalSectorCount * BytesPerSector;

    public ulong AvailableFreeSpace => 0; // TODO: Calculate

    public string Type => FatTypeValue switch
    {
        FatType.Fat12 => "FAT12",
        FatType.Fat16 => "FAT16",
        FatType.Fat32 => "FAT32",
        _ => "Unknown"
    };

    public string Label { get; set; } = "DISK";
    public MountFlags Flags { get; set; }

    #endregion

    #region IFileSystemFormatter Implementation

    /// <summary>
    /// Formats the partition as FAT32.
    /// </summary>
    public void Format(Partition partition, bool fast)
    {
        Serial.WriteString("[FAT] Formatting partition as FAT32...\n");

        // Calculate parameters
        BytesPerSector = (uint)partition.BlockSize;
        NumberOfFATs = 2;
        TotalSectorCount = (uint)partition.BlockCount;

        // Determine sectors per cluster based on size
        if (TotalSectorCount < 66600)
        {
            throw new Exception("Partition too small for FAT32 (minimum 33MB)");
        }
        else if (TotalSectorCount < 532480)
        {
            SectorsPerCluster = 1;
        }
        else if (TotalSectorCount < 16777216)
        {
            SectorsPerCluster = 8;
        }
        else if (TotalSectorCount < 33554432)
        {
            SectorsPerCluster = 16;
        }
        else if (TotalSectorCount < 67108864)
        {
            SectorsPerCluster = 32;
        }
        else
        {
            SectorsPerCluster = 64;
        }

        BytesPerCluster = BytesPerSector * SectorsPerCluster;
        ReservedSectorCount = 32;
        RootEntryCount = 0;

        // Calculate FAT size
        ulong fatElementSize = 4;
        ulong reservedClusCnt = 2;
        ulong numerator = TotalSectorCount - ReservedSectorCount + reservedClusCnt * SectorsPerCluster;
        ulong denominator = SectorsPerCluster * BytesPerSector / fatElementSize + NumberOfFATs;
        FatSectorCount = (uint)(numerator / denominator + 1);

        Serial.WriteString("[FAT] Creating BPB...\n");

        // Create BPB
        Span<byte> bpb = new byte[512];

        // Jump instruction
        bpb[0] = 0xEB;
        bpb[1] = 0xFE;
        bpb[2] = 0x90;

        // OEM name "MSWIN4.1"
        byte[] oemName = "MSWIN4.1"u8.ToArray();
        oemName.CopyTo(bpb.Slice(3, 8));

        // BPB
        BitConverter.TryWriteBytes(bpb.Slice(11, 2), (ushort)BytesPerSector);
        bpb[13] = (byte)SectorsPerCluster;
        BitConverter.TryWriteBytes(bpb.Slice(14, 2), (ushort)ReservedSectorCount);
        bpb[16] = (byte)NumberOfFATs;
        BitConverter.TryWriteBytes(bpb.Slice(17, 2), (ushort)RootEntryCount);

        if (TotalSectorCount > 0xFFFF)
        {
            BitConverter.TryWriteBytes(bpb.Slice(19, 2), (ushort)0);
            BitConverter.TryWriteBytes(bpb.Slice(32, 4), TotalSectorCount);
        }
        else
        {
            BitConverter.TryWriteBytes(bpb.Slice(19, 2), (ushort)TotalSectorCount);
            BitConverter.TryWriteBytes(bpb.Slice(32, 4), 0u);
        }

        bpb[21] = 0xF8; // Media type: Hard disk

        // FAT32 Extended BPB
        BitConverter.TryWriteBytes(bpb.Slice(36, 4), FatSectorCount);
        bpb[40] = 0; // Ext flags
        BitConverter.TryWriteBytes(bpb.Slice(42, 2), (ushort)0); // Version
        BitConverter.TryWriteBytes(bpb.Slice(44, 4), 2u); // Root cluster
        BitConverter.TryWriteBytes(bpb.Slice(48, 2), (ushort)1); // FSInfo sector
        BitConverter.TryWriteBytes(bpb.Slice(50, 2), (ushort)6); // Backup boot sector
        bpb[64] = 0x80; // Drive number
        bpb[66] = 0x29; // Signature

        // Volume serial
        BitConverter.TryWriteBytes(bpb.Slice(67, 4), 0x01020304u);

        // Volume label "COSMOSDISK "
        byte[] volumeLabel = "COSMOSDISK "u8.ToArray();
        volumeLabel.CopyTo(bpb.Slice(71, 11));

        // File system type "FAT32   "
        byte[] fsType = "FAT32   "u8.ToArray();
        fsType.CopyTo(bpb.Slice(82, 8));

        // Boot signature
        bpb[510] = 0x55;
        bpb[511] = 0xAA;

        Serial.WriteString("[FAT] Writing BPB to sector 0...\n");
        partition.WriteBlock(0, 1, bpb);

        Serial.WriteString("[FAT] Writing backup BPB to sector 6...\n");
        partition.WriteBlock(6, 1, bpb);

        // Create FSInfo structure
        Serial.WriteString("[FAT] Creating FSInfo...\n");
        Span<byte> fsInfo = new byte[512];
        BitConverter.TryWriteBytes(fsInfo.Slice(0, 4), 0x41615252u); // Lead signature
        BitConverter.TryWriteBytes(fsInfo.Slice(484, 4), 0x61417272u); // Struct signature
        BitConverter.TryWriteBytes(fsInfo.Slice(488, 4), 0xFFFFFFFFu); // Free cluster count (unknown)
        BitConverter.TryWriteBytes(fsInfo.Slice(492, 4), 0xFFFFFFFFu); // Next free cluster (unknown)
        BitConverter.TryWriteBytes(fsInfo.Slice(508, 4), 0xAA550000u); // Trail signature
        fsInfo[510] = 0x55;
        fsInfo[511] = 0xAA;

        partition.WriteBlock(1, 1, fsInfo);
        partition.WriteBlock(7, 1, fsInfo);

        // Create first FAT sector
        Serial.WriteString("[FAT] Creating FAT table...\n");
        Span<byte> firstFat = new byte[512];
        BitConverter.TryWriteBytes(firstFat.Slice(0, 4), 0x0FFFFFFFu); // Reserved cluster 1
        BitConverter.TryWriteBytes(firstFat.Slice(4, 4), 0x0FFFFFFFu); // Reserved cluster 2
        BitConverter.TryWriteBytes(firstFat.Slice(8, 4), 0x0FFFFFFFu); // Root directory EOC
        firstFat[0] = 0xF8; // Media type

        // Write FAT to both copies
        for (uint i = 0; i < NumberOfFATs; i++)
        {
            partition.WriteBlock(ReservedSectorCount + i * FatSectorCount, 1, firstFat);
        }

        // Clear remaining FAT sectors if not fast format
        if (!fast)
        {
            Serial.WriteString("[FAT] Clearing FAT sectors (slow format)...\n");
            Span<byte> emptyBlock = new byte[512];
            for (uint sector = 1; sector < FatSectorCount; sector++)
            {
                for (uint fat = 0; fat < NumberOfFATs; fat++)
                {
                    partition.WriteBlock(ReservedSectorCount + fat * FatSectorCount + sector, 1, emptyBlock);
                }
            }
        }

        // Update internal state
        RootCluster = 2;
        DataSector = ReservedSectorCount + NumberOfFATs * FatSectorCount;
        FatTypeValue = FatType.Fat32;

        Serial.WriteString("[FAT] Format complete!\n");
    }

    /// <summary>
    /// Creates a formatted FAT32 filesystem on the partition.
    /// </summary>
    public static FatFileSystem CreateFormatted(Partition partition, string root, bool fast = true)
    {
        var fs = new FatFileSystem(partition, root, true);
        fs.Format(partition, fast);
        return fs;
    }

    #endregion
}
