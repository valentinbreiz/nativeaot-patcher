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
    private readonly uint _firstCluster;
    private readonly FatAttributes _attributes;
    private readonly Dictionary<string, string> _metadata;

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

    public ulong Size { get; private set; }
    public string Path { get; }
    public string Name { get; private set; }

    public IReadOnlyDictionary<string, string> Metadata => _metadata;

    public uint FirstCluster => _firstCluster;

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
/// Stream implementation for reading FAT files.
/// </summary>
public class FatFileStream : Stream
{
    private readonly FatFileSystem _fileSystem;
    private readonly FatDirectoryEntry _entry;
    private readonly List<uint> _clusterChain;
    private long _position;

    public FatFileStream(FatFileSystem fileSystem, FatDirectoryEntry entry)
    {
        _fileSystem = fileSystem;
        _entry = entry;
        _position = 0;

        var fat = fileSystem.GetFat();
        _clusterChain = fat?.GetClusterChain(entry.FirstCluster) ?? new List<uint>();
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => (long)_entry.Size;

    public override long Position
    {
        get => _position;
        set => _position = value;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position >= Length)
        {
            return 0;
        }

        int bytesRead = 0;
        int bytesPerCluster = (int)_fileSystem.BytesPerCluster;

        while (count > 0 && _position < Length)
        {
            int clusterIndex = (int)(_position / bytesPerCluster);
            int clusterOffset = (int)(_position % bytesPerCluster);

            if (clusterIndex >= _clusterChain.Count)
            {
                break;
            }

            uint cluster = _clusterChain[clusterIndex];
            byte[] clusterData = _fileSystem.NewClusterArray();
            _fileSystem.ReadCluster(cluster, clusterData);

            long remaining = Length - _position;
            int bytesToRead = Math.Min(count, bytesPerCluster - clusterOffset);
            if (bytesToRead > remaining)
            {
                bytesToRead = (int)remaining;
            }

            Array.Copy(clusterData, clusterOffset, buffer, offset, bytesToRead);

            offset += bytesToRead;
            count -= bytesToRead;
            _position += bytesToRead;
            bytesRead += bytesToRead;
        }

        return bytesRead;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        _position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => Length + offset,
            _ => _position
        };
        return _position;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
