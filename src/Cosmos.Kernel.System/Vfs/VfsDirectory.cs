// This code is licensed under MIT license (see LICENSE for details)

using System;
using Cosmos.Kernel.HAL.Vfs;

namespace Cosmos.Kernel.System.Vfs;

/// <summary>
/// Managed handle for a directory node supporting lookup and mutation operations.
/// </summary>
public interface IVfsDirectoryHandle : IVfsNodeHandle
{
    bool TryLookup(ReadOnlySpan<char> name, out IVfsNodeHandle? child);

    bool TryCreateFile(ReadOnlySpan<char> name, ModeEnum mode, out IVfsNodeHandle? child);

    bool TryCreateDirectory(ReadOnlySpan<char> name, ModeEnum mode, out IVfsDirectoryHandle? child);

    bool TrySymlink(ReadOnlySpan<char> name, ReadOnlySpan<char> target, out IVfsNodeHandle? child);

    bool TryUnlink(ReadOnlySpan<char> name);

    bool TryRemoveDirectory(ReadOnlySpan<char> name);

    bool TryRename(ReadOnlySpan<char> oldName, IVfsDirectoryHandle newParent, ReadOnlySpan<char> newName);

    bool TrySetAttr(SetAttrFlags flags, in VfsStat attributes);
}

/// <summary>
/// Default directory handle that delegates to HAL inode operations.
/// </summary>
internal sealed class VfsDirectoryHandle : IVfsDirectoryHandle
{
    public VfsDirectoryHandle(string name, IVfsInode inode)
    {
        Name = name;
        Inode = inode;
    }

    public string Name { get; }

    public IVfsInode Inode { get; }

    public bool TryLookup(ReadOnlySpan<char> name, out IVfsNodeHandle? child)
    {
        child = null;
        if (Inode.InodeOperations == null)
        {
            return false;
        }

        if (!Inode.InodeOperations.Lookup(Inode, name, out IVfsInode? result) || result == null)
        {
            return false;
        }

        child = VfsManager.WrapNode(name.ToString(), result);
        return child != null;
    }

    public bool TryCreateFile(ReadOnlySpan<char> name, ModeEnum mode, out IVfsNodeHandle? child)
    {
        child = null;
        if (Inode.InodeOperations == null)
        {
            return false;
        }

        if (!Inode.InodeOperations.Create(Inode, name, mode, out IVfsInode? created) || created == null)
        {
            return false;
        }

        child = VfsManager.WrapNode(name.ToString(), created);
        return child != null;
    }

    public bool TryCreateDirectory(ReadOnlySpan<char> name, ModeEnum mode, out IVfsDirectoryHandle? child)
    {
        child = null;
        if (Inode.InodeOperations == null)
        {
            return false;
        }

        if (!Inode.InodeOperations.Mkdir(Inode, name, mode, out IVfsInode? created) || created == null)
        {
            return false;
        }

        child = new VfsDirectoryHandle(name.ToString(), created);
        return true;
    }

    public bool TrySymlink(ReadOnlySpan<char> name, ReadOnlySpan<char> target, out IVfsNodeHandle? child)
    {
        child = null;
        if (Inode.InodeOperations == null)
        {
            return false;
        }

        if (!Inode.InodeOperations.Symlink(Inode, name, target, out IVfsInode? created) || created == null)
        {
            return false;
        }

        child = VfsManager.WrapNode(name.ToString(), created);
        return child != null;
    }

    public bool TryUnlink(ReadOnlySpan<char> name)
    {
        if (Inode.InodeOperations == null)
        {
            return false;
        }

        return Inode.InodeOperations.Unlink(Inode, name);
    }

    public bool TryRemoveDirectory(ReadOnlySpan<char> name)
    {
        if (Inode.InodeOperations == null)
        {
            return false;
        }

        return Inode.InodeOperations.Rmdir(Inode, name);
    }

    public bool TryRename(ReadOnlySpan<char> oldName, IVfsDirectoryHandle newParent, ReadOnlySpan<char> newName)
    {
        if (Inode.InodeOperations == null)
        {
            return false;
        }

        return Inode.InodeOperations.Rename(Inode, oldName, newParent.Inode, newName);
    }

    public bool TrySetAttr(SetAttrFlags flags, in VfsStat attributes)
    {
        if (Inode.InodeOperations == null)
        {
            return false;
        }

        return Inode.InodeOperations.SetAttr(Inode, flags, attributes);
    }

    public bool TryStat(out VfsStat stat)
    {
        stat = default;
        if (Inode.InodeOperations == null)
        {
            return false;
        }

        return Inode.InodeOperations.GetAttr(Inode, out stat);
    }
}
