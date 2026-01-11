// This code is licensed under MIT license (see LICENSE for details)

using System;
using System.Collections.Generic;

namespace Cosmos.Kernel.System.FileSystem.MemFs;

/// <summary>
/// Inode operations implementation for MemFs.
/// </summary>
internal class MemFsInodeOperations : IInodeOperations
{
    private readonly MemFsFileSystemOperations _fileSystem;

    public MemFsInodeOperations(MemFsFileSystemOperations fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public IInode? Lookup(IInode parent, string name)
    {
        if (parent is not MemFsInode memParent)
            return null;

        if (!memParent.IsDirectory)
            return null;

        // Handle . and ..
        if (name == ".")
            return parent;
        if (name == "..")
            return parent.Parent ?? parent;

        foreach (MemFsInode child in memParent.Children)
        {
            if (child.Name == name)
            {
                // If it's a symlink, resolve it
                if (child.IsSymlink)
                {
                    return ResolveSymlink(child);
                }
                return child;
            }
        }

        return null;
    }

    public IInode CreateFile(IInode parent, string name)
    {
        if (parent is not MemFsInode memParent)
            throw new ArgumentException("Parent must be a MemFsInode.", nameof(parent));

        if (!memParent.IsDirectory)
            throw new InvalidOperationException("Parent must be a directory.");

        // Check if file already exists
        foreach (MemFsInode child in memParent.Children)
        {
            if (child.Name == name)
                throw new InvalidOperationException($"File '{name}' already exists.");
        }

        ulong inodeNumber = _fileSystem.AllocateInodeNumber();
        MemFsInode newInode = new MemFsInode(inodeNumber, _fileSystem, name, InodeType.File, memParent);
        memParent.AddChild(newInode);
        _fileSystem.RegisterInode(newInode);

        return newInode;
    }

    public IInode CreateDirectory(IInode parent, string name)
    {
        if (parent is not MemFsInode memParent)
            throw new ArgumentException("Parent must be a MemFsInode.", nameof(parent));

        if (!memParent.IsDirectory)
            throw new InvalidOperationException("Parent must be a directory.");

        // Check if directory already exists
        foreach (MemFsInode child in memParent.Children)
        {
            if (child.Name == name)
                throw new InvalidOperationException($"Directory '{name}' already exists.");
        }

        ulong inodeNumber = _fileSystem.AllocateInodeNumber();
        MemFsInode newInode = new MemFsInode(inodeNumber, _fileSystem, name, InodeType.Directory, memParent);
        memParent.AddChild(newInode);
        _fileSystem.RegisterInode(newInode);

        return newInode;
    }

    public void Unlink(IInode inode)
    {
        if (inode is not MemFsInode memInode)
            throw new ArgumentException("Inode must be a MemFsInode.", nameof(inode));

        // Cannot unlink root
        if (memInode.Parent == null)
            throw new InvalidOperationException("Cannot unlink root directory.");

        // Cannot unlink non-empty directory
        if (memInode.IsDirectory && memInode.Children.Count > 0)
            throw new InvalidOperationException("Cannot unlink non-empty directory.");

        IInode? parentInode = memInode.Parent;
        if (parentInode is MemFsInode parent)
        {
            parent.RemoveChild(memInode);
        }

        _fileSystem.UnregisterInode(memInode);
    }

    public void Rename(IInode oldInode, IInode newParent, string newName)
    {
        if (oldInode is not MemFsInode memOldInode)
            throw new ArgumentException("Old inode must be a MemFsInode.", nameof(oldInode));

        if (newParent is not MemFsInode memNewParent)
            throw new ArgumentException("New parent must be a MemFsInode.", nameof(newParent));

        if (!memNewParent.IsDirectory)
            throw new InvalidOperationException("New parent must be a directory.");

        // Check if target name already exists
        foreach (MemFsInode child in memNewParent.Children)
        {
            if (child.Name == newName && child != memOldInode)
                throw new InvalidOperationException($"Target name '{newName}' already exists.");
        }

        // Remove from old parent
        IInode? oldParentInode = memOldInode.Parent;
        if (oldParentInode is MemFsInode oldParent)
        {
            oldParent.RemoveChild(memOldInode);
        }

        // Add to new parent
        memNewParent.AddChild(memOldInode);
        memOldInode.SetParent(memNewParent);
        memOldInode.SetName(newName);
    }

    public List<IInode> ReadDirectory(IInode directory)
    {
        if (directory is not MemFsInode memDirectory)
            throw new ArgumentException("Directory must be a MemFsInode.", nameof(directory));

        if (!memDirectory.IsDirectory)
            throw new InvalidOperationException("Inode must be a directory.");

        List<IInode> result = new List<IInode>();
        foreach (MemFsInode child in memDirectory.Children)
        {
            result.Add(child);
        }

        return result;
    }

    public IFileOperations? GetFileOperations(IInode inode)
    {
        if (inode is not MemFsInode memInode)
            return null;

        if (memInode.IsFile)
            return _fileSystem.FileOperations;

        return null;
    }

    /// <summary>
    /// Creates a symlink.
    /// </summary>
    public IInode CreateSymlink(IInode parent, string name, string target)
    {
        if (parent is not MemFsInode memParent)
            throw new ArgumentException("Parent must be a MemFsInode.", nameof(parent));

        if (!memParent.IsDirectory)
            throw new InvalidOperationException("Parent must be a directory.");

        // Check if symlink already exists
        foreach (MemFsInode child in memParent.Children)
        {
            if (child.Name == name)
                throw new InvalidOperationException($"Symlink '{name}' already exists.");
        }

        ulong inodeNumber = _fileSystem.AllocateInodeNumber();
        MemFsInode newInode = new MemFsInode(inodeNumber, _fileSystem, name, InodeType.Symlink, memParent);
        newInode.SetSymlinkTarget(target);
        memParent.AddChild(newInode);
        _fileSystem.RegisterInode(newInode);

        return newInode;
    }

    /// <summary>
    /// Resolves a symlink to its target inode.
    /// </summary>
    private IInode? ResolveSymlink(MemFsInode symlink)
    {
        if (!symlink.IsSymlink || symlink.SymlinkTarget == null)
            return null;

        string target = symlink.SymlinkTarget;
        
        // Handle absolute paths
        if (target.StartsWith("/"))
        {
            return _fileSystem.GetInode(target);
        }

        // Handle relative paths
        IInode? current = symlink.Parent;
        string[] parts = target.Split('/', StringSplitOptions.RemoveEmptyEntries);

        foreach (string part in parts)
        {
            if (part == ".")
                continue;
            if (part == "..")
            {
                current = current?.Parent;
                continue;
            }

            if (current == null)
                return null;

            current = Lookup(current, part);
            if (current == null)
                return null;
        }

        return current;
    }
}
