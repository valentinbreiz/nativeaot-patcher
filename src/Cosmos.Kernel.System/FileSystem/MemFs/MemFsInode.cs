// This code is licensed under MIT license (see LICENSE for details)

using System;
using System.Collections.Generic;

namespace Cosmos.Kernel.System.FileSystem.MemFs;

/// <summary>
/// In-memory inode implementation for MemFs.
/// </summary>
internal class MemFsInode : IInode
{
    private readonly Dictionary<string, string> _metadata = new();
    private byte[] _data;
    private readonly List<MemFsInode> _children = new();
    private MemFsInode? _parent;
    private string _name;
    private InodeType _type;
    private string? _symlinkTarget;

    public MemFsInode(ulong inodeNumber, IFileSystemOperations fileSystem, string name, InodeType type, MemFsInode? parent = null)
    {
        InodeNumber = inodeNumber;
        FileSystem = fileSystem;
        _name = name;
        _type = type;
        _parent = parent;
        _data = type == InodeType.File ? new byte[0] : Array.Empty<byte>();
        _symlinkTarget = null;
    }

    public ulong InodeNumber { get; }
    public IFileSystemOperations FileSystem { get; }
    public string Path => BuildPath();
    public string Name => _name;
    public ulong Size => _type == InodeType.File ? (ulong)_data.Length : 0;
    public bool IsDirectory => _type == InodeType.Directory;
    public bool IsFile => _type == InodeType.File;
    public bool IsSymlink => _type == InodeType.Symlink;
    public IFileOperations? FileOperations => IsFile ? ((MemFsFileSystemOperations)FileSystem).FileOperations : null;
    public IInode? Parent => _parent;

    public string? SymlinkTarget => _symlinkTarget;

    internal InodeType Type => _type;
    internal byte[] Data => _data;
    internal List<MemFsInode> Children => _children;

    internal void SetName(string name)
    {
        _name = name;
    }

    internal void SetData(byte[] data)
    {
        if (_type != InodeType.File)
            throw new InvalidOperationException("Cannot set data on non-file inode.");
        _data = data;
    }

    internal void SetSymlinkTarget(string target)
    {
        if (_type != InodeType.Symlink)
            throw new InvalidOperationException("Cannot set symlink target on non-symlink inode.");
        _symlinkTarget = target;
    }

    internal void AddChild(MemFsInode child)
    {
        if (_type != InodeType.Directory)
            throw new InvalidOperationException("Cannot add child to non-directory inode.");
        _children.Add(child);
    }

    internal void RemoveChild(MemFsInode child)
    {
        _children.Remove(child);
    }

    internal void SetParent(MemFsInode? parent)
    {
        _parent = parent;
    }

    private string BuildPath()
    {
        if (_parent == null)
            return "/";

        string parentPath = _parent.Path;
        if (parentPath == "/")
            return "/" + _name;
        return parentPath + "/" + _name;
    }
}

/// <summary>
/// Type of inode in MemFs.
/// </summary>
internal enum InodeType
{
    File,
    Directory,
    Symlink
}
