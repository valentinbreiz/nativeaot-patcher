// This code is licensed under MIT license (see LICENSE for details)

using System;
using System.Collections.Generic;
using Cosmos.Kernel.HAL.Vfs;

namespace Cosmos.Kernel.System.Filesystems.Fat;

internal sealed class FatInodeOperations : IInodeOperations
{
    private readonly FatSuperblock _superblock;

    public FatInodeOperations(FatSuperblock superblock)
    {
        _superblock = superblock;
    }

    public bool Lookup(IVfsInode dir, ReadOnlySpan<char> name, out IVfsInode? child)
    {
        child = null;
        if (dir is not FatInode parent || !parent.IsDirectory)
        {
            return false;
        }

        byte[] data = _superblock.ReadDirectoryData(parent);
        List<FatDirEntry> entries = FatDirectory.Parse(data, _superblock.Boot.Type == FatType.Fat32);

        string targetName = name.ToString();
        for (int i = 0; i < entries.Count; i++)
        {
            FatDirEntry entry = entries[i];
            if (entry.IsVolumeId)
            {
                continue;
            }

            if (FatDirectory.NameEqualsIgnoreCase(entry.Name, targetName) || FatDirectory.NameEqualsIgnoreCase(entry.ShortName, targetName))
            {
                FatInode found = _superblock.GetOrCreateInode(parent, entry);
                child = found;
                return true;
            }
        }

        return false;
    }

    public bool ReadDir(IVfsInode dir, out IList<IVfsInode> entries)
    {
        entries = Array.Empty<IVfsInode>();
        if (dir is not FatInode parent || !parent.IsDirectory)
        {
            return false;
        }

        byte[] data = _superblock.ReadDirectoryData(parent);
        List<FatDirEntry> raw = FatDirectory.Parse(data, _superblock.Boot.Type == FatType.Fat32);
        List<IVfsInode> result = new(raw.Count);
        for (int i = 0; i < raw.Count; i++)
        {
            FatDirEntry entry = raw[i];
            if (entry.IsVolumeId)
            {
                continue;
            }
            if (entry.Name == "." || entry.Name == "..")
            {
                continue;
            }

            result.Add(_superblock.GetOrCreateInode(parent, entry));
        }
        entries = result;
        return true;
    }

    public bool Create(IVfsInode dir, ReadOnlySpan<char> name, ModeEnum mode, out IVfsInode? inode)
    {
        inode = null;
        if (dir is not FatInode parent || !parent.IsDirectory)
        {
            return false;
        }

        FatAttr attr = FatAttributes.ToFatAttr(mode) & ~FatAttr.Directory;
        if (!_superblock.AllocateDirectoryEntry(parent, name, attr, 0, 0, out FatInode? created))
        {
            return false;
        }

        inode = created;
        return true;
    }

    public bool Mkdir(IVfsInode dir, ReadOnlySpan<char> name, ModeEnum mode, out IVfsInode? inode)
    {
        inode = null;
        if (dir is not FatInode parent || !parent.IsDirectory)
        {
            return false;
        }

        uint cluster = _superblock.Fat.AllocateChain(1);
        if (cluster == 0)
        {
            return false;
        }

        Span<byte> clusterBuffer = new byte[_superblock.Boot.BytesPerCluster];
        WriteDotEntries(clusterBuffer, cluster, parent.FirstCluster);
        _superblock.WriteCluster(cluster, clusterBuffer);

        FatAttr attr = FatAttributes.ToFatAttr(mode) | FatAttr.Directory;
        if (!_superblock.AllocateDirectoryEntry(parent, name, attr, cluster, 0, out FatInode? created))
        {
            _superblock.Fat.Free(cluster);
            return false;
        }

        inode = created;
        return true;
    }

    public bool Symlink(IVfsInode dir, ReadOnlySpan<char> name, ReadOnlySpan<char> target, out IVfsInode? inode)
    {
        inode = null;
        return false;
    }

    public bool Unlink(IVfsInode dir, ReadOnlySpan<char> name)
    {
        if (dir is not FatInode parent || !parent.IsDirectory)
        {
            return false;
        }

        if (!_superblock.FindChildEntry(parent, name, out FatDirEntry? match) || match == null)
        {
            return false;
        }

        if ((match.Attributes & FatAttr.Directory) != 0)
        {
            return false;
        }

        if (match.FirstCluster >= 2)
        {
            _superblock.Fat.Free(match.FirstCluster);
        }
        _superblock.RemoveDirectoryEntry(parent, match);
        _superblock.ForgetInode(match.FirstCluster);
        return true;
    }

    public bool Rmdir(IVfsInode dir, ReadOnlySpan<char> name)
    {
        if (dir is not FatInode parent || !parent.IsDirectory)
        {
            return false;
        }

        if (!_superblock.FindChildEntry(parent, name, out FatDirEntry? match) || match == null)
        {
            return false;
        }

        if ((match.Attributes & FatAttr.Directory) == 0)
        {
            return false;
        }

        FatInode target = _superblock.GetOrCreateInode(parent, match);
        if (!_superblock.IsDirectoryEmpty(target))
        {
            return false;
        }

        if (target.FirstCluster >= 2)
        {
            _superblock.Fat.Free(target.FirstCluster);
        }
        _superblock.RemoveDirectoryEntry(parent, match);
        _superblock.ForgetInode(match.FirstCluster);
        return true;
    }

    public bool Rename(IVfsInode oldParent, ReadOnlySpan<char> oldName, IVfsInode newParent, ReadOnlySpan<char> newName)
    {
        if (oldParent is not FatInode op || newParent is not FatInode np)
        {
            return false;
        }

        if (!_superblock.FindChildEntry(op, oldName, out FatDirEntry? match) || match == null)
        {
            return false;
        }

        FatAttr attr = match.Attributes;
        uint firstCluster = match.FirstCluster;
        uint size = match.Size;

        _superblock.RemoveDirectoryEntry(op, match);
        _superblock.ForgetInode(match.FirstCluster);

        if (!_superblock.AllocateDirectoryEntry(np, newName, attr, firstCluster, size, out _))
        {
            return false;
        }

        return true;
    }

    public bool GetAttr(IVfsInode inode, out VfsStat stat)
    {
        stat = default;
        if (inode is not FatInode node)
        {
            return false;
        }

        stat.Ino = node.FirstCluster;
        stat.Mode = FatAttributes.ToMode(node.Attributes);
        stat.NLink = 1;
        stat.Uid = 0;
        stat.Gid = 0;
        stat.Rdev = 0;
        stat.Size = node.Size;
        stat.BlkSize = _superblock.BlockSize;
        stat.Blocks = (ulong)node.ResolveChain().Count * _superblock.Boot.SectorsPerCluster;
        stat.Atime = new VfsTimespec(0, 0);
        stat.Mtime = new VfsTimespec(0, 0);
        stat.Ctime = new VfsTimespec(0, 0);
        return true;
    }

    public bool SetAttr(IVfsInode inode, SetAttrFlags flags, in VfsStat attributes)
    {
        if (inode is not FatInode node)
        {
            return false;
        }

        if ((flags & SetAttrFlags.Size) != 0)
        {
            uint newSize = (uint)attributes.Size;
            if (newSize < node.Size)
            {
                _superblock.TruncateChain(node, newSize);
                node.Size = newSize;
            }
            else if (newSize > node.Size)
            {
                if (!_superblock.GrowZeroFilled(node, newSize))
                {
                    return false;
                }
            }
        }

        if ((flags & SetAttrFlags.Mode) != 0)
        {
            FatAttr cleared = FatAttributes.ToFatAttr(attributes.Mode);
            FatAttr preservedDir = node.Attributes & FatAttr.Directory;
            node.Attributes = cleared | preservedDir;
        }

        _superblock.UpdateInodeEntry(node);
        return true;
    }


    private static void WriteDotEntries(Span<byte> clusterBuffer, uint selfCluster, uint parentCluster)
    {
        clusterBuffer.Clear();
        FatDirectory.WriteShortEntry(clusterBuffer, 0, ".          ", FatAttr.Directory, selfCluster, 0);
        FatDirectory.WriteShortEntry(clusterBuffer, FatDirectory.EntrySize, "..         ", FatAttr.Directory, parentCluster, 0);
    }
}
