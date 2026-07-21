// This code is licensed under MIT license (see LICENSE for details)

using System.Diagnostics.CodeAnalysis;
using Cosmos.Kernel.HAL.Vfs;

namespace Cosmos.Kernel.System.Vfs;

/// <summary>
/// Managed handle for a directory node supporting lookup and mutation operations.
/// </summary>
public interface IVfsDirectoryHandle : IVfsNodeHandle
{
    bool TryReadDir(out IReadOnlyList<IVfsInode> entries);

    bool TryLookup(ReadOnlySpan<char> name, [NotNullWhen(true)] out IVfsNodeHandle? child);

    bool TryCreateFile(ReadOnlySpan<char> name, ModeEnum mode, [NotNullWhen(true)] out IVfsNodeHandle? child);

    bool TryCreateDirectory(ReadOnlySpan<char> name, ModeEnum mode, [NotNullWhen(true)] out IVfsDirectoryHandle? child);

    bool TrySymlink(ReadOnlySpan<char> name, ReadOnlySpan<char> target, [NotNullWhen(true)] out IVfsNodeHandle? child);

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

    public bool TryReadDir(out IReadOnlyList<IVfsInode> entries)
    {
        return Inode.InodeOperations.ReadDir(Inode, out entries);
    }

    public bool TryLookup(ReadOnlySpan<char> name, [NotNullWhen(true)] out IVfsNodeHandle? child)
    {
        if (!Inode.InodeOperations.Lookup(Inode, name, out IVfsInode? result) || result == null)
        {
            child = null;
            return false;
        }

        child = VfsManager.WrapNode(name.ToString(), result);
        return child != null;
    }

    public bool TryCreateFile(ReadOnlySpan<char> name, ModeEnum mode, [NotNullWhen(true)] out IVfsNodeHandle? child)
    {
        if (!Inode.InodeOperations.Create(Inode, name, mode, out IVfsInode? created) || created == null)
        {
            child = null;
            return false;
        }

        child = VfsManager.WrapNode(name.ToString(), created);
        return child != null;
    }

    public bool TryCreateDirectory(ReadOnlySpan<char> name, ModeEnum mode, [NotNullWhen(true)] out IVfsDirectoryHandle? child)
    {
        if (!Inode.InodeOperations.Mkdir(Inode, name, mode, out IVfsInode? created) || created == null)
        {
            child = null;
            return false;
        }

        child = new VfsDirectoryHandle(name.ToString(), created);
        return true;
    }

    public bool TrySymlink(ReadOnlySpan<char> name, ReadOnlySpan<char> target, [NotNullWhen(true)] out IVfsNodeHandle? child)
    {

        if (!Inode.InodeOperations.Symlink(Inode, name, target, out IVfsInode? created) || created == null)
        {
            child = null;
            return false;
        }

        child = VfsManager.WrapNode(name.ToString(), created);
        return child != null;
    }

    public bool TryUnlink(ReadOnlySpan<char> name)
    {
        return Inode.InodeOperations.Unlink(Inode, name);
    }

    public bool TryRemoveDirectory(ReadOnlySpan<char> name)
    {
        return Inode.InodeOperations.Rmdir(Inode, name);
    }

    public bool TryRename(ReadOnlySpan<char> oldName, IVfsDirectoryHandle newParent, ReadOnlySpan<char> newName)
    {
        return Inode.InodeOperations.Rename(Inode, oldName, newParent.Inode, newName);
    }

    public bool TrySetAttr(SetAttrFlags flags, in VfsStat attributes)
    {
        return Inode.InodeOperations.SetAttr(Inode, flags, attributes);
    }

    public bool TryStat(out VfsStat stat)
    {
        return Inode.InodeOperations.GetAttr(Inode, out stat);
    }
}
