// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.HAL.Vfs;

/// <summary>
/// Directory and inode metadata operations.
/// </summary>
public interface IInodeOperations
{
    bool Lookup(IVfsInode dir, ReadOnlySpan<char> name, out IVfsInode? child);

    bool ReadDir(IVfsInode dir, IList<VfsDirectoryEntry> entries);

    bool Create(IVfsInode dir, ReadOnlySpan<char> name, ModeEnum mode, out IVfsInode? inode);

    bool Mkdir(IVfsInode dir, ReadOnlySpan<char> name, ModeEnum mode, out IVfsInode? inode);

    bool Symlink(
        IVfsInode dir,
        ReadOnlySpan<char> name,
        ReadOnlySpan<char> target,
        out IVfsInode? inode);

    bool Unlink(IVfsInode dir, ReadOnlySpan<char> name);

    bool Rmdir(IVfsInode dir, ReadOnlySpan<char> name);

    bool Rename(
        IVfsInode oldParent,
        ReadOnlySpan<char> oldName,
        IVfsInode newParent,
        ReadOnlySpan<char> newName);

    bool GetAttr(IVfsInode inode, out VfsStat stat);

    bool SetAttr(IVfsInode inode, SetAttrFlags flags, in VfsStat attributes);
}
