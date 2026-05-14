// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.HAL.Interfaces.Devices;
using Cosmos.Kernel.HAL.Vfs;

namespace Cosmos.Kernel.System.Filesystems.Fat;

internal sealed class FatSuperblock : IVfsSuperblock
{
    private readonly IBlockDevice _device;
    private readonly Dictionary<uint, FatInode> _inodeCache = new();

    public FatBootSector Boot { get; }
    public FatTable Fat { get; }
    public FatInodeOperations InodeOps { get; }
    public FatFileOperations FileOps { get; }
    public FatSuperblockOperations SuperOps { get; }

    public IVfsInode Root { get; }
    public ISuperblockOperations SuperOperations => SuperOps;
    public long BlockSize => Boot.BytesPerSector;
    public ulong MaxNameLength => 255;

    public FatSuperblock(IBlockDevice device, FatBootSector boot)
    {
        _device = device;
        Boot = boot;
        Fat = new FatTable(device, boot);
        InodeOps = new FatInodeOperations(this);
        FileOps = new FatFileOperations(this);
        SuperOps = new FatSuperblockOperations();

        uint rootCluster = boot.Type == FatType.Fat32 ? boot.RootCluster : 0;
        FatInode root = new(this, "/", FatAttr.Directory, rootCluster, 0, parent: null, dirEntryByteOffset: -1, dirEntrySlotCount: 0);
        Root = root;
    }

    public void Drop()
    {
        _inodeCache.Clear();
    }

    public void ReadCluster(uint cluster, Span<byte> data)
    {
        ulong lba = Boot.ClusterToLba(cluster);
        _device.ReadBlock(lba, Boot.SectorsPerCluster, data);
    }

    public void WriteCluster(uint cluster, Span<byte> data)
    {
        ulong lba = Boot.ClusterToLba(cluster);
        _device.WriteBlock(lba, Boot.SectorsPerCluster, data);
    }

    public byte[] ReadDirectoryData(FatInode dir)
    {
        if (dir.IsFixedRoot)
        {
            byte[] data = new byte[Boot.RootSectorCount * Boot.BytesPerSector];
            _device.ReadBlock(Boot.RootStartLba, Boot.RootSectorCount, data);
            return data;
        }

        List<uint> chain = dir.ResolveChain();
        byte[] full = new byte[chain.Count * Boot.BytesPerCluster];
        Span<byte> buffer = full;
        for (int i = 0; i < chain.Count; i++)
        {
            ReadCluster(chain[i], buffer.Slice(i * (int)Boot.BytesPerCluster, (int)Boot.BytesPerCluster));
        }
        return full;
    }

    public void WriteDirectoryData(FatInode dir, ReadOnlySpan<byte> data)
    {
        if (dir.IsFixedRoot)
        {
            _device.WriteBlock(Boot.RootStartLba, Boot.RootSectorCount, data.ToArray());
            return;
        }

        List<uint> chain = dir.ResolveChain();
        for (int i = 0; i < chain.Count; i++)
        {
            byte[] sliceCopy = new byte[Boot.BytesPerCluster];
            data.Slice(i * (int)Boot.BytesPerCluster, (int)Boot.BytesPerCluster).CopyTo(sliceCopy);
            WriteCluster(chain[i], sliceCopy);
        }
    }

    public FatInode GetOrCreateInode(FatInode parent, FatDirEntry entry)
    {
        if (entry.FirstCluster >= 2 && _inodeCache.TryGetValue(entry.FirstCluster, out FatInode? cached))
        {
            cached.Name = entry.Name;
            cached.Size = entry.Size;
            cached.Attributes = entry.Attributes;
            cached.Parent = parent;
            cached.DirEntryByteOffset = entry.ByteOffset;
            cached.DirEntrySlotCount = entry.LfnEntryCount + 1;
            return cached;
        }

        FatInode node = new(
            this,
            entry.Name,
            entry.Attributes,
            entry.FirstCluster,
            entry.Size,
            parent,
            entry.ByteOffset,
            entry.LfnEntryCount + 1);

        if (entry.FirstCluster >= 2)
        {
            _inodeCache[entry.FirstCluster] = node;
        }
        return node;
    }

    public void ForgetInode(uint firstCluster)
    {
        if (firstCluster >= 2)
        {
            _inodeCache.Remove(firstCluster);
        }
    }

    public bool FindChildEntry(FatInode parent, ReadOnlySpan<char> name, out FatDirEntry? match)
    {
        match = null;
        byte[] data = ReadDirectoryData(parent);
        List<FatDirEntry> entries = FatDirectory.Parse(data);
        string target = name.ToString();
        for (int i = 0; i < entries.Count; i++)
        {
            FatDirEntry entry = entries[i];
            if (entry.IsVolumeId)
            {
                continue;
            }
            if (NameEquals(entry.Name, target) || NameEquals(entry.ShortName, target))
            {
                match = entry;
                return true;
            }
        }
        return false;
    }

    public bool IsDirectoryEmpty(FatInode dir)
    {
        byte[] data = ReadDirectoryData(dir);
        List<FatDirEntry> entries = FatDirectory.Parse(data);
        for (int i = 0; i < entries.Count; i++)
        {
            FatDirEntry entry = entries[i];
            if (entry.IsVolumeId)
            {
                continue;
            }
            if (entry.Name == "." || entry.Name == "..")
            {
                continue;
            }
            return false;
        }
        return true;
    }

    public bool AllocateDirectoryEntry(
        FatInode parent,
        ReadOnlySpan<char> name,
        FatAttr attr,
        uint firstCluster,
        uint size,
        out FatInode? created)
    {
        created = null;
        string longName = name.ToString();
        if (string.IsNullOrEmpty(longName))
        {
            return false;
        }

        int lfnCount = FatDirectory.LfnEntryCountFor(longName);
        int slots = lfnCount + 1;

        byte[] data = ReadDirectoryData(parent);
        int slot = FatDirectory.FindFreeRun(data, slots);
        if (slot < 0)
        {
            if (parent.IsFixedRoot)
            {
                return false;
            }
            data = GrowDirectory(parent, data, slots);
            slot = FatDirectory.FindFreeRun(data, slots);
            if (slot < 0)
            {
                return false;
            }
        }

        Span<char> shortBuffer = stackalloc char[11];
        FatDirectory.BuildShortName(longName, shortBuffer);

        if (lfnCount > 0)
        {
            FatDirectory.WriteLfnEntries(data, slot, longName, shortBuffer);
        }
        int eightThreeOffset = slot + lfnCount * FatDirectory.EntrySize;
        FatDirectory.WriteShortEntry(data, eightThreeOffset, shortBuffer, attr, firstCluster, size);

        WriteDirectoryData(parent, data);

        FatInode node = new(
            this,
            longName,
            attr,
            firstCluster,
            size,
            parent,
            eightThreeOffset,
            slots);
        if (firstCluster >= 2)
        {
            _inodeCache[firstCluster] = node;
        }
        created = node;
        return true;
    }

    public void RemoveDirectoryEntry(FatInode parent, FatDirEntry match)
    {
        byte[] data = ReadDirectoryData(parent);
        int slots = match.LfnEntryCount + 1;
        int start = match.ByteOffset - match.LfnEntryCount * FatDirectory.EntrySize;
        FatDirectory.MarkDeleted(data, start, slots);
        WriteDirectoryData(parent, data);
    }

    public void UpdateInodeEntry(FatInode inode)
    {
        if (inode.Parent == null || inode.DirEntryByteOffset < 0)
        {
            return;
        }

        byte[] data = ReadDirectoryData(inode.Parent);
        int offset = inode.DirEntryByteOffset;
        if (offset + FatDirectory.EntrySize > data.Length)
        {
            return;
        }

        BitConverter.TryWriteBytes(data.AsSpan(offset + 20, 2), (ushort)((inode.FirstCluster >> 16) & 0xFFFFu));
        BitConverter.TryWriteBytes(data.AsSpan(offset + 26, 2), (ushort)(inode.FirstCluster & 0xFFFFu));
        BitConverter.TryWriteBytes(data.AsSpan(offset + 28, 4), inode.Size);
        WriteDirectoryData(inode.Parent, data);
    }

    public void TruncateChain(FatInode inode, uint newSize)
    {
        uint clusterSize = Boot.BytesPerCluster;
        uint clustersToKeep = (newSize + clusterSize - 1) / clusterSize;

        List<uint> chain = inode.ResolveChain();
        if (clustersToKeep == 0)
        {
            if (inode.FirstCluster >= 2)
            {
                Fat.Free(inode.FirstCluster);
            }
            inode.FirstCluster = 0;
            chain.Clear();
            return;
        }

        if (chain.Count <= clustersToKeep)
        {
            return;
        }

        uint newTail = chain[(int)clustersToKeep - 1];
        for (int i = (int)clustersToKeep; i < chain.Count; i++)
        {
            Fat.Set(chain[i], FatTable.FreeCluster);
        }
        Fat.Set(newTail, Fat.EndOfChainMarker());
        chain.RemoveRange((int)clustersToKeep, chain.Count - (int)clustersToKeep);
    }

    public bool GrowZeroFilled(FatInode inode, uint newSize)
    {
        long startPos = inode.Size;
        uint zeros = newSize - inode.Size;
        Span<byte> emptyBuffer = new byte[Boot.BytesPerCluster];

        uint clusterSize = Boot.BytesPerCluster;
        long endPosition = newSize;
        long clustersNeeded = (endPosition + clusterSize - 1) / clusterSize;
        List<uint> chain = inode.ResolveChain();

        while (chain.Count < clustersNeeded)
        {
            uint added = Fat.AllocateChain(1);
            if (added == 0)
            {
                return false;
            }
            if (chain.Count == 0)
            {
                inode.FirstCluster = added;
            }
            else
            {
                Fat.Set(chain[^1], added);
            }
            chain.Add(added);
            WriteCluster(added, emptyBuffer);
        }

        long pos = startPos;
        while (pos < newSize)
        {
            long clusterIndex = pos / clusterSize;
            long intra = pos % clusterSize;
            uint cluster = chain[(int)clusterIndex];
            ReadCluster(cluster, emptyBuffer);
            long clear = clusterSize - intra;
            if (pos + clear > newSize)
            {
                clear = newSize - pos;
            }
            emptyBuffer.Slice((int)intra, (int)clear).Clear();
            WriteCluster(cluster, emptyBuffer);
            pos += clear;
        }

        inode.Size = newSize;
        return true;
    }

    private byte[] GrowDirectory(FatInode parent, byte[] currentData, int slotsNeeded)
    {
        if (parent.IsFixedRoot)
        {
            return currentData;
        }

        List<uint> chain = parent.ResolveChain();
        uint newCluster = Fat.AllocateChain(1);
        if (newCluster == 0)
        {
            return currentData;
        }

        if (chain.Count == 0)
        {
            parent.FirstCluster = newCluster;
        }
        else
        {
            Fat.Set(chain[^1], newCluster);
        }
        chain.Add(newCluster);

        Span<byte> emptyCluster = new byte[Boot.BytesPerCluster];
        WriteCluster(newCluster, emptyCluster);

        byte[] grown = new byte[currentData.Length + (int)Boot.BytesPerCluster];
        Buffer.BlockCopy(currentData, 0, grown, 0, currentData.Length);
        return grown;
    }

    private static bool NameEquals(string a, string b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }
        for (int i = 0; i < a.Length; i++)
        {
            char ac = a[i];
            char bc = b[i];
            if (ac >= 'a' && ac <= 'z')
            {
                ac = (char)(ac - 32);
            }
            if (bc >= 'a' && bc <= 'z')
            {
                bc = (char)(bc - 32);
            }
            if (ac != bc)
            {
                return false;
            }
        }
        return true;
    }
}
