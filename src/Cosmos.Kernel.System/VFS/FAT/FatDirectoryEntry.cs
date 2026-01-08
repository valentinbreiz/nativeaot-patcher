// This code is licensed under MIT license (see LICENSE for details)

using System.Text;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.System.VFS.Enums;
using Cosmos.Kernel.System.VFS.Interfaces;

namespace Cosmos.Kernel.System.VFS.FAT;

/// <summary>
/// FAT directory entry attributes.
/// </summary>
[Flags]
public enum FatAttributes : byte
{
    ReadOnly = 0x01,
    Hidden = 0x02,
    System = 0x04,
    VolumeId = 0x08,
    Directory = 0x10,
    Archive = 0x20,
    LongName = ReadOnly | Hidden | System | VolumeId
}

/// <summary>
/// FAT directory entry implementation.
/// </summary>
public class FatDirectoryEntry : IDirectoryEntry
{
    private readonly FatFileSystem _fileSystem;
    private readonly FatDirectoryEntry? _parent;
    private uint _firstCluster;
    private FatAttributes _attributes;
    private readonly Dictionary<string, string> _metadata;

    /// <summary>
    /// Entry offset within parent directory cluster (for updating metadata).
    /// </summary>
    internal uint EntryCluster { get; set; }
    internal int EntryOffset { get; set; }

    public FatDirectoryEntry(
        FatFileSystem fileSystem,
        FatDirectoryEntry? parent,
        string name,
        string path,
        long size,
        uint firstCluster,
        FatAttributes attributes = FatAttributes.Directory)
    {
        _fileSystem = fileSystem;
        _parent = parent;
        Name = name;
        Path = path;
        Size = (ulong)size;
        _firstCluster = firstCluster;
        _attributes = attributes;
        _metadata = new Dictionary<string, string>();
    }

    public DirectoryEntryType Type =>
        (_attributes & FatAttributes.Directory) != 0
            ? DirectoryEntryType.Directory
            : DirectoryEntryType.File;

    public ulong Size { get; internal set; }
    public string Path { get; }
    public string Name { get; private set; }

    public IReadOnlyDictionary<string, string> Metadata => _metadata;

    public uint FirstCluster
    {
        get => _firstCluster;
        internal set => _firstCluster = value;
    }

    public FatFileSystem FileSystem => _fileSystem;
    public FatDirectoryEntry? Parent => _parent;

    public void SetName(string name)
    {
        Name = name;
    }

    public ulong GetUsedSpace() => Size;

    public void SetMetadata(string key, string? value)
    {
        if (value == null)
        {
            _metadata.Remove(key);
        }
        else
        {
            _metadata[key] = value;
        }
    }

    /// <summary>
    /// Reads the contents of a directory.
    /// </summary>
    public List<IDirectoryEntry> ReadDirectoryContents()
    {
        var entries = new List<IDirectoryEntry>();

        if (Type != DirectoryEntryType.Directory)
        {
            return entries;
        }

        var fat = _fileSystem.GetFat();
        if (fat == null)
        {
            return entries;
        }

        // Get cluster chain for this directory
        var clusters = fat.GetClusterChain(_firstCluster);

        string longName = "";

        foreach (uint cluster in clusters)
        {
            byte[] clusterData = _fileSystem.NewClusterArray();
            _fileSystem.ReadCluster(cluster, clusterData);

            // Each directory entry is 32 bytes
            for (int offset = 0; offset < clusterData.Length; offset += 32)
            {
                byte firstByte = clusterData[offset];

                // End of directory
                if (firstByte == 0x00)
                {
                    return entries;
                }

                // Deleted entry
                if (firstByte == 0xE5)
                {
                    continue;
                }

                FatAttributes attr = (FatAttributes)clusterData[offset + 11];

                // Long filename entry
                if ((attr & FatAttributes.LongName) == FatAttributes.LongName)
                {
                    longName = ParseLongNameEntry(clusterData, offset) + longName;
                    continue;
                }

                // Skip volume label
                if ((attr & FatAttributes.VolumeId) != 0)
                {
                    continue;
                }

                // Parse short name entry
                var entry = ParseShortNameEntry(clusterData, offset, longName);
                if (entry != null)
                {
                    entries.Add(entry);
                }

                longName = "";
            }
        }

        return entries;
    }

    private FatDirectoryEntry? ParseShortNameEntry(byte[] data, int offset, string longName)
    {
        // Get short name (8.3 format)
        string shortName = Encoding.ASCII.GetString(data, offset, 8).TrimEnd();
        string extension = Encoding.ASCII.GetString(data, offset + 8, 3).TrimEnd();

        FatAttributes attr = (FatAttributes)data[offset + 11];

        // Get first cluster
        uint firstClusterHigh = BitConverter.ToUInt16(data, offset + 20);
        uint firstClusterLow = BitConverter.ToUInt16(data, offset + 26);
        uint firstCluster = (firstClusterHigh << 16) | firstClusterLow;

        // Get file size
        uint fileSize = BitConverter.ToUInt32(data, offset + 28);

        // Use long name if available, otherwise construct from short name
        string name;
        if (!string.IsNullOrEmpty(longName))
        {
            name = longName;
        }
        else
        {
            name = shortName;
            if (!string.IsNullOrEmpty(extension))
            {
                name += "." + extension;
            }
        }

        // Skip . and .. entries
        if (name == "." || name == "..")
        {
            return null;
        }

        string fullPath = Path.TrimEnd('/') + "/" + name;

        return new FatDirectoryEntry(
            _fileSystem,
            this,
            name,
            fullPath,
            fileSize,
            firstCluster,
            attr);
    }

    private string ParseLongNameEntry(byte[] data, int offset)
    {
        var chars = new char[13];
        int idx = 0;

        // Characters 1-5 (offset 1-10)
        for (int i = 1; i <= 10 && idx < 13; i += 2)
        {
            ushort ch = BitConverter.ToUInt16(data, offset + i);
            if (ch == 0 || ch == 0xFFFF)
            {
                break;
            }
            chars[idx++] = (char)ch;
        }

        // Characters 6-11 (offset 14-25)
        for (int i = 14; i <= 25 && idx < 13; i += 2)
        {
            ushort ch = BitConverter.ToUInt16(data, offset + i);
            if (ch == 0 || ch == 0xFFFF)
            {
                break;
            }
            chars[idx++] = (char)ch;
        }

        // Characters 12-13 (offset 28-31)
        for (int i = 28; i <= 30 && idx < 13; i += 2)
        {
            ushort ch = BitConverter.ToUInt16(data, offset + i);
            if (ch == 0 || ch == 0xFFFF)
            {
                break;
            }
            chars[idx++] = (char)ch;
        }

        return new string(chars, 0, idx);
    }

    /// <summary>
    /// Creates a new file in this directory.
    /// </summary>
    public FatDirectoryEntry CreateFile(string name)
    {
        if (Type != DirectoryEntryType.Directory)
        {
            throw new InvalidOperationException("Cannot create file in a file entry");
        }

        Serial.WriteString("[FAT] Creating file: ");
        Serial.WriteString(name);
        Serial.WriteString("\n");

        var fat = _fileSystem.GetFat();
        if (fat == null)
        {
            throw new Exception("FAT table not initialized");
        }

        // Allocate a cluster for the file (empty file gets one cluster)
        uint fileCluster = fat.AllocateClusterChain(1);
        if (fileCluster == 0)
        {
            throw new Exception("No free clusters available");
        }

        // Clear the cluster
        byte[] emptyCluster = _fileSystem.NewClusterArray();
        _fileSystem.WriteCluster(fileCluster, emptyCluster);

        // Create directory entry
        string fullPath = Path.TrimEnd('/') + "/" + name;
        var newEntry = new FatDirectoryEntry(
            _fileSystem,
            this,
            name,
            fullPath,
            0,
            fileCluster,
            FatAttributes.Archive);

        // Write the entry to this directory
        WriteNewEntry(name, fileCluster, 0, FatAttributes.Archive);

        return newEntry;
    }

    /// <summary>
    /// Creates a new subdirectory in this directory.
    /// </summary>
    public FatDirectoryEntry CreateDirectory(string name)
    {
        if (Type != DirectoryEntryType.Directory)
        {
            throw new InvalidOperationException("Cannot create directory in a file entry");
        }

        Serial.WriteString("[FAT] Creating directory: ");
        Serial.WriteString(name);
        Serial.WriteString("\n");

        var fat = _fileSystem.GetFat();
        if (fat == null)
        {
            throw new Exception("FAT table not initialized");
        }

        // Allocate a cluster for the directory
        uint dirCluster = fat.AllocateClusterChain(1);
        if (dirCluster == 0)
        {
            throw new Exception("No free clusters available");
        }

        // Initialize the directory cluster with . and .. entries
        byte[] dirData = _fileSystem.NewClusterArray();

        // Create "." entry (points to self)
        CreateDotEntry(dirData, 0, ".", dirCluster);

        // Create ".." entry (points to parent)
        CreateDotEntry(dirData, 32, "..", _firstCluster);

        // Write directory cluster
        _fileSystem.WriteCluster(dirCluster, dirData);

        // Create directory entry
        string fullPath = Path.TrimEnd('/') + "/" + name;
        var newEntry = new FatDirectoryEntry(
            _fileSystem,
            this,
            name,
            fullPath,
            0,
            dirCluster,
            FatAttributes.Directory);

        // Write the entry to this directory
        WriteNewEntry(name, dirCluster, 0, FatAttributes.Directory);

        return newEntry;
    }

    /// <summary>
    /// Deletes this entry from the filesystem.
    /// </summary>
    public void Delete()
    {
        if (_parent == null)
        {
            throw new InvalidOperationException("Cannot delete root directory");
        }

        Serial.WriteString("[FAT] Deleting: ");
        Serial.WriteString(Name);
        Serial.WriteString("\n");

        var fat = _fileSystem.GetFat();
        if (fat == null)
        {
            throw new Exception("FAT table not initialized");
        }

        // If it's a directory, ensure it's empty
        if (Type == DirectoryEntryType.Directory)
        {
            var contents = ReadDirectoryContents();
            if (contents.Count > 0)
            {
                throw new InvalidOperationException("Directory is not empty");
            }
        }

        // Free the cluster chain
        if (_firstCluster >= 2)
        {
            fat.FreeClusterChain(_firstCluster);
        }

        // Mark entry as deleted in parent directory
        MarkAsDeleted();
    }

    /// <summary>
    /// Sets the size of the file and updates cluster allocation.
    /// </summary>
    public void SetSize(long newSize)
    {
        if (Type != DirectoryEntryType.File)
        {
            throw new InvalidOperationException("Cannot set size on directory");
        }

        Serial.WriteString("[FAT] SetSize: ");
        Serial.WriteNumber((ulong)newSize);
        Serial.WriteString("\n");

        var fat = _fileSystem.GetFat();
        if (fat == null)
        {
            throw new Exception("FAT table not initialized");
        }

        long currentSize = (long)Size;
        uint bytesPerCluster = _fileSystem.BytesPerCluster;

        uint currentClusters = currentSize == 0 ? 0 : (uint)((currentSize + bytesPerCluster - 1) / bytesPerCluster);
        uint newClusters = newSize == 0 ? 0 : (uint)((newSize + bytesPerCluster - 1) / bytesPerCluster);

        if (newClusters > currentClusters)
        {
            // Need to allocate more clusters
            uint additionalClusters = newClusters - currentClusters;

            if (_firstCluster == 0 || currentClusters == 0)
            {
                // Allocate new chain
                uint newChain = fat.AllocateClusterChain(newClusters);
                if (newChain == 0)
                {
                    throw new Exception("No free clusters");
                }
                _firstCluster = newChain;
            }
            else
            {
                // Extend existing chain
                var chain = fat.GetClusterChain(_firstCluster);
                uint lastCluster = chain[^1];
                uint newChain = fat.AllocateClusterChain(additionalClusters);
                if (newChain == 0)
                {
                    throw new Exception("No free clusters");
                }
                fat.SetFatEntry(lastCluster, newChain);
            }
        }
        else if (newClusters < currentClusters && currentClusters > 0)
        {
            // Need to free clusters
            var chain = fat.GetClusterChain(_firstCluster);
            if (newClusters == 0)
            {
                fat.FreeClusterChain(_firstCluster);
                _firstCluster = 0;
            }
            else
            {
                // Mark end of chain at new position
                fat.SetFatEntry(chain[(int)newClusters - 1], FatTable.EndOfChain);
                // Free remaining clusters
                for (int i = (int)newClusters; i < chain.Count; i++)
                {
                    fat.SetFatEntry(chain[i], FatTable.FreeCluster);
                }
            }
        }

        Size = (ulong)newSize;

        // Update directory entry on disk
        UpdateDirectoryEntry();
    }

    /// <summary>
    /// Gets the FAT table for this entry's cluster chain.
    /// </summary>
    public uint[] GetFatTable()
    {
        var fat = _fileSystem.GetFat();
        if (fat == null || _firstCluster < 2)
        {
            return Array.Empty<uint>();
        }
        return fat.GetClusterChain(_firstCluster).ToArray();
    }

    #region Private Helper Methods

    private void CreateDotEntry(byte[] data, int offset, string name, uint cluster)
    {
        // Name (8 bytes)
        byte[] nameBytes = Encoding.ASCII.GetBytes(name.PadRight(8, ' '));
        Array.Copy(nameBytes, 0, data, offset, 8);

        // Extension (3 bytes) - spaces
        data[offset + 8] = 0x20;
        data[offset + 9] = 0x20;
        data[offset + 10] = 0x20;

        // Attributes - directory
        data[offset + 11] = (byte)FatAttributes.Directory;

        // Reserved
        data[offset + 12] = 0;
        data[offset + 13] = 0;

        // Creation time/date (use zeros for simplicity)
        // offset 14-17: creation time
        // offset 18-19: creation date
        // offset 20-21: first cluster high
        BitConverter.TryWriteBytes(new Span<byte>(data, offset + 20, 2), (ushort)(cluster >> 16));

        // offset 22-23: last access date
        // offset 24-25: last modification time
        // offset 26-27: first cluster low
        BitConverter.TryWriteBytes(new Span<byte>(data, offset + 26, 2), (ushort)(cluster & 0xFFFF));

        // offset 28-31: file size (0 for directories)
        BitConverter.TryWriteBytes(new Span<byte>(data, offset + 28, 4), 0u);
    }

    private void WriteNewEntry(string name, uint cluster, uint size, FatAttributes attributes)
    {
        var fat = _fileSystem.GetFat();
        if (fat == null)
        {
            throw new Exception("FAT table not initialized");
        }

        // Get cluster chain for this directory
        var clusters = fat.GetClusterChain(_firstCluster);

        // Find a free entry slot
        foreach (uint dirCluster in clusters)
        {
            byte[] clusterData = _fileSystem.NewClusterArray();
            _fileSystem.ReadCluster(dirCluster, clusterData);

            for (int offset = 0; offset < clusterData.Length; offset += 32)
            {
                byte firstByte = clusterData[offset];

                // Free entry (never used or deleted)
                if (firstByte == 0x00 || firstByte == 0xE5)
                {
                    // Write the entry here
                    WriteShortNameEntry(clusterData, offset, name, cluster, size, attributes);
                    _fileSystem.WriteCluster(dirCluster, clusterData);
                    return;
                }
            }
        }

        // No free entry found, need to allocate new cluster for directory
        uint newCluster = fat.AllocateClusterChain(1);
        if (newCluster == 0)
        {
            throw new Exception("No free clusters for directory expansion");
        }

        // Link new cluster to chain
        uint lastCluster = clusters[^1];
        fat.SetFatEntry(lastCluster, newCluster);

        // Initialize new cluster and write entry
        byte[] newClusterData = _fileSystem.NewClusterArray();
        WriteShortNameEntry(newClusterData, 0, name, cluster, size, attributes);
        _fileSystem.WriteCluster(newCluster, newClusterData);
    }

    private void WriteShortNameEntry(byte[] data, int offset, string name, uint cluster, uint size, FatAttributes attributes)
    {
        // Convert name to 8.3 format
        string shortName;
        string extension = "";

        int dotIndex = name.LastIndexOf('.');
        if (dotIndex > 0 && (attributes & FatAttributes.Directory) == 0)
        {
            shortName = name[..dotIndex].ToUpperInvariant();
            extension = name[(dotIndex + 1)..].ToUpperInvariant();
        }
        else
        {
            shortName = name.ToUpperInvariant();
        }

        // Truncate to 8.3 limits
        if (shortName.Length > 8) shortName = shortName[..8];
        if (extension.Length > 3) extension = extension[..3];

        // Write name (8 bytes, space padded)
        byte[] nameBytes = Encoding.ASCII.GetBytes(shortName.PadRight(8));
        Array.Copy(nameBytes, 0, data, offset, 8);

        // Write extension (3 bytes, space padded)
        byte[] extBytes = Encoding.ASCII.GetBytes(extension.PadRight(3));
        Array.Copy(extBytes, 0, data, offset + 8, 3);

        // Attributes
        data[offset + 11] = (byte)attributes;

        // Reserved and time fields (zeros for simplicity)
        for (int i = 12; i < 20; i++)
        {
            data[offset + i] = 0;
        }

        // First cluster high
        BitConverter.TryWriteBytes(new Span<byte>(data, offset + 20, 2), (ushort)(cluster >> 16));

        // Last modification time/date (zeros)
        data[offset + 22] = 0;
        data[offset + 23] = 0;
        data[offset + 24] = 0;
        data[offset + 25] = 0;

        // First cluster low
        BitConverter.TryWriteBytes(new Span<byte>(data, offset + 26, 2), (ushort)(cluster & 0xFFFF));

        // File size
        BitConverter.TryWriteBytes(new Span<byte>(data, offset + 28, 4), size);
    }

    private void MarkAsDeleted()
    {
        if (_parent == null) return;

        var fat = _fileSystem.GetFat();
        if (fat == null) return;

        var clusters = fat.GetClusterChain(_parent._firstCluster);

        foreach (uint cluster in clusters)
        {
            byte[] clusterData = _fileSystem.NewClusterArray();
            _fileSystem.ReadCluster(cluster, clusterData);

            for (int offset = 0; offset < clusterData.Length; offset += 32)
            {
                // Check if this is our entry by comparing first cluster
                uint entryClusterHigh = BitConverter.ToUInt16(clusterData, offset + 20);
                uint entryClusterLow = BitConverter.ToUInt16(clusterData, offset + 26);
                uint entryCluster = (entryClusterHigh << 16) | entryClusterLow;

                if (entryCluster == _firstCluster && clusterData[offset] != 0xE5)
                {
                    // Mark as deleted
                    clusterData[offset] = 0xE5;
                    _fileSystem.WriteCluster(cluster, clusterData);
                    return;
                }
            }
        }
    }

    private void UpdateDirectoryEntry()
    {
        if (_parent == null) return;

        var fat = _fileSystem.GetFat();
        if (fat == null) return;

        var clusters = fat.GetClusterChain(_parent._firstCluster);

        foreach (uint cluster in clusters)
        {
            byte[] clusterData = _fileSystem.NewClusterArray();
            _fileSystem.ReadCluster(cluster, clusterData);

            for (int offset = 0; offset < clusterData.Length; offset += 32)
            {
                // Skip empty/deleted entries
                byte firstByte = clusterData[offset];
                if (firstByte == 0x00 || firstByte == 0xE5)
                    continue;

                // Get short name from entry
                string shortName = Encoding.ASCII.GetString(clusterData, offset, 8).TrimEnd();
                string ext = Encoding.ASCII.GetString(clusterData, offset + 8, 3).TrimEnd();

                // Build expected short name from our file name
                string expectedName;
                string expectedExt = "";
                int dotPos = Name.LastIndexOf('.');
                if (dotPos >= 0 && dotPos < Name.Length - 1)
                {
                    expectedName = Name[..dotPos];
                    expectedExt = Name[(dotPos + 1)..];
                }
                else
                {
                    expectedName = Name;
                }

                // Truncate to 8.3 format for comparison
                if (expectedName.Length > 8) expectedName = expectedName[..8];
                if (expectedExt.Length > 3) expectedExt = expectedExt[..3];

                // Compare names (case-insensitive)
                if (string.Equals(shortName, expectedName.ToUpper(), StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(ext, expectedExt.ToUpper(), StringComparison.OrdinalIgnoreCase))
                {
                    // Found our entry - update cluster and size
                    BitConverter.TryWriteBytes(new Span<byte>(clusterData, offset + 20, 2), (ushort)(_firstCluster >> 16));
                    BitConverter.TryWriteBytes(new Span<byte>(clusterData, offset + 26, 2), (ushort)(_firstCluster & 0xFFFF));
                    BitConverter.TryWriteBytes(new Span<byte>(clusterData, offset + 28, 4), (uint)Size);

                    _fileSystem.WriteCluster(cluster, clusterData);
                    Serial.WriteString("[FAT] UpdateDirectoryEntry: Updated ");
                    Serial.WriteString(Name);
                    Serial.WriteString(" size=");
                    Serial.WriteNumber(Size);
                    Serial.WriteString(" cluster=");
                    Serial.WriteNumber(_firstCluster);
                    Serial.WriteString("\n");
                    return;
                }
            }
        }

        Serial.WriteString("[FAT] UpdateDirectoryEntry: Entry not found for ");
        Serial.WriteString(Name);
        Serial.WriteString("\n");
    }

    #endregion
}

/// <summary>
/// FAT file entry implementation.
/// </summary>
public class FatFileEntry : FatDirectoryEntry, IFileEntry
{
    private readonly FatFileSystem _fileSystem;

    public FatFileEntry(
        FatFileSystem fileSystem,
        FatDirectoryEntry? parent,
        string name,
        string path,
        long size,
        uint firstCluster)
        : base(fileSystem, parent, name, path, size, firstCluster, FatAttributes.Archive)
    {
        _fileSystem = fileSystem;
    }

    public Stream GetFileStream()
    {
        return new FatFileStream(_fileSystem, this);
    }
}

/// <summary>
/// Stream implementation for reading and writing FAT files.
/// </summary>
public class FatFileStream : Stream
{
    private readonly FatFileSystem _fileSystem;
    private readonly FatDirectoryEntry _entry;
    private uint[] _fatTable;
    private long _position;
    private long _size;

    public FatFileStream(FatFileSystem fileSystem, FatDirectoryEntry entry)
    {
        _fileSystem = fileSystem;
        _entry = entry;
        _position = 0;
        _size = (long)entry.Size;
        _fatTable = entry.GetFatTable();
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => true;
    public override long Length => _size;

    public override long Position
    {
        get => _position;
        set
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            _position = value;
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }
        if (offset + count > buffer.Length)
        {
            throw new ArgumentException("Invalid offset length");
        }

        if (_fatTable.Length == 0 || _fatTable[0] == 0)
        {
            return 0;
        }

        if (_position >= _size)
        {
            return 0;
        }

        long maxReadableBytes = _size - _position;
        long xCount = count;
        long xOffset = offset;

        if (xCount > maxReadableBytes)
        {
            xCount = maxReadableBytes;
        }

        int bytesPerCluster = (int)_fileSystem.BytesPerCluster;
        byte[] clusterData = _fileSystem.NewClusterArray();

        while (xCount > 0)
        {
            int clusterIdx = (int)(_position / bytesPerCluster);
            int posInCluster = (int)(_position % bytesPerCluster);

            if (clusterIdx >= _fatTable.Length)
            {
                break;
            }

            _fileSystem.ReadCluster(_fatTable[clusterIdx], clusterData);

            long readSize;
            if (posInCluster + xCount > bytesPerCluster)
            {
                readSize = bytesPerCluster - posInCluster;
            }
            else
            {
                readSize = xCount;
            }

            Array.Copy(clusterData, posInCluster, buffer, (int)xOffset, (int)readSize);

            xOffset += readSize;
            xCount -= readSize;
            _position += readSize;
        }

        return (int)(xOffset - offset);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }
        if (offset + count > buffer.Length)
        {
            throw new ArgumentException("Invalid offset length");
        }

        long xCount = count;
        int bytesPerCluster = (int)_fileSystem.BytesPerCluster;
        long xOffset = offset;

        long totalLength = _position + xCount;

        // Extend file if needed
        if (totalLength > _size)
        {
            SetLength(totalLength);
        }

        byte[] clusterData = _fileSystem.NewClusterArray();

        while (xCount > 0)
        {
            long writeSize;
            int clusterIdx = (int)(_position / bytesPerCluster);
            int posInCluster = (int)(_position % bytesPerCluster);

            if (posInCluster + xCount > bytesPerCluster)
            {
                writeSize = bytesPerCluster - posInCluster;
            }
            else
            {
                writeSize = xCount;
            }

            // Read existing cluster data
            _fileSystem.ReadCluster(_fatTable[clusterIdx], clusterData);

            // Copy new data into cluster
            Array.Copy(buffer, (int)xOffset, clusterData, posInCluster, (int)writeSize);

            // Write cluster back
            _fileSystem.WriteCluster(_fatTable[clusterIdx], clusterData);

            xOffset += writeSize;
            xCount -= writeSize;
            _position += writeSize;
        }
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        _position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _size + offset,
            _ => _position
        };
        return _position;
    }

    public override void SetLength(long value)
    {
        _entry.SetSize(value);
        _size = value;
        _fatTable = _entry.GetFatTable();
    }

    public override void Flush()
    {
        // All writes are immediately flushed to disk
    }
}
