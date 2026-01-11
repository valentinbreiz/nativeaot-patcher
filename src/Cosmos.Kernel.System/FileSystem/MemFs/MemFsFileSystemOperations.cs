// This code is licensed under MIT license (see LICENSE for details)

using System;
using System.Collections.Generic;

namespace Cosmos.Kernel.System.FileSystem.MemFs;

/// <summary>
/// File system operations implementation for MemFs.
/// </summary>
internal class MemFsFileSystemOperations : IFileSystemOperations
{
    private readonly MemFsInode _root;
    private readonly MemFsInodeOperations _inodeOperations;
    private readonly MemFsFileOperations _fileOperations;
    private readonly Dictionary<ulong, MemFsInode> _inodes = new();
    private readonly Dictionary<string, MemFsInode> _inodesByPath = new();
    private ulong _nextInodeNumber = 1;
    private string _mountPoint;

    public MemFsFileSystemOperations(string mountPoint)
    {
        _mountPoint = mountPoint;
        _fileOperations = new MemFsFileOperations();
        _inodeOperations = new MemFsInodeOperations(this);
        
        // Create root directory
        _root = new MemFsInode(0, this, "", InodeType.Directory, null);
        _inodes[0] = _root;
        _inodesByPath["/"] = _root;
    }

    public IInode GetRootInode() => _root;
    public IInodeOperations InodeOperations => _inodeOperations;
    public string MountPoint => _mountPoint;
    public ulong TotalSize => ulong.MaxValue; // In-memory, so "unlimited"
    public ulong AvailableFreeSpace => ulong.MaxValue; // In-memory, so "unlimited"
    public string FileSystemType => "MemFs";

    internal MemFsFileOperations FileOperations => _fileOperations;

    public IInode? GetInode(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        // Normalize path
        path = NormalizePath(path);

        // Check cache
        if (_inodesByPath.TryGetValue(path, out MemFsInode? cached))
            return cached;

        // Resolve path
        if (path == "/")
            return _root;

        string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        IInode? current = _root;

        foreach (string part in parts)
        {
            if (current == null)
                return null;

            current = _inodeOperations.Lookup(current, part);
            if (current == null)
                return null;
        }

        // Cache the result if it's a MemFsInode
        if (current is MemFsInode memInode)
        {
            _inodesByPath[path] = memInode;
        }

        return current;
    }

    public void Sync()
    {
        // In-memory file system doesn't need syncing
    }

    internal ulong AllocateInodeNumber()
    {
        return _nextInodeNumber++;
    }

    internal void RegisterInode(MemFsInode inode)
    {
        _inodes[inode.InodeNumber] = inode;
        string path = inode.Path;
        if (!string.IsNullOrEmpty(path))
        {
            _inodesByPath[path] = inode;
        }
    }

    internal void UnregisterInode(MemFsInode inode)
    {
        _inodes.Remove(inode.InodeNumber);
        string path = inode.Path;
        if (!string.IsNullOrEmpty(path))
        {
            _inodesByPath.Remove(path);
        }
    }

    private static string NormalizePath(string path)
    {
        // Remove trailing slashes (except for root)
        path = path.TrimEnd('/');
        if (string.IsNullOrEmpty(path))
            return "/";

        // Ensure leading slash
        if (!path.StartsWith("/"))
            path = "/" + path;

        // Remove duplicate slashes
        while (path.Contains("//"))
            path = path.Replace("//", "/");

        return path;
    }
}
