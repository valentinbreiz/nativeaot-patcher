// This code is licensed under MIT license (see LICENSE for details)

using System;
using System.Collections.Generic;
using Cosmos.Kernel.HAL.Vfs;

namespace Cosmos.Kernel.System.Vfs;

/// <summary>
/// Central entry point for registering filesystem drivers and resolving VFS paths.
/// </summary>
public static class VfsManager
{
    private sealed class VfsOpenFile : IVfsOpenFile
    {
        public VfsOpenFile(string name, IVfsInode inode, IFileOperations operations)
        {
            Name = name;
            Inode = inode;
            Operations = operations;
            Position = 0;
        }

        public string Name { get; }

        public IVfsInode Inode { get; }

        public IFileOperations Operations { get; }

        public long Position { get; set; }
    }

    /// <summary>
    /// Represents a mounted filesystem instance.
    /// </summary>
    public sealed class VfsMount
    {
        public VfsMount(string name, string mountPoint, IVfsFilesystemType filesystemType, IVfsSuperblock superblock)
        {
            Name = name;
            MountPoint = mountPoint;
            FilesystemType = filesystemType;
            Superblock = superblock;
        }

        public string Name { get; }

        public string MountPoint { get; }

        public IVfsFilesystemType FilesystemType { get; }

        public IVfsSuperblock Superblock { get; }
    }

    private static readonly Dictionary<string, IVfsFilesystemType> s_registeredTypes = new(StringComparer.Ordinal);
    private static readonly List<VfsMount> s_mounts = new();

    /// <summary>
    /// Register a filesystem driver by name.
    /// </summary>
    /// <returns><c>true</c> when registration succeeds; <c>false</c> if name is invalid, driver is null, or already registered.</returns>
    public static bool RegisterFilesystem(string name, IVfsFilesystemType filesystemType)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (filesystemType == null)
        {
            return false;
        }

        if (s_registeredTypes.ContainsKey(name))
        {
            return false;
        }

        s_registeredTypes.Add(name, filesystemType);
        return true;
    }

    /// <summary>
    /// Mount a registered filesystem driver at a mount point.
    /// </summary>
    /// <param name="name">Registered filesystem name.</param>
    /// <param name="source">Driver-specific backing store identifier.</param>
    /// <param name="flags">Mount flags.</param>
    /// <param name="mountPoint">Mount point (normalized to leading /, no trailing /).</param>
    /// <param name="mount">Resulting mount data.</param>
    /// <returns><c>true</c> on success, <c>false</c> if driver is missing or mount fails.</returns>
    public static bool TryMount(string name, ReadOnlySpan<char> source, MountFlags flags, string mountPoint, out VfsMount? mount)
    {
        mount = null;

        if (!s_registeredTypes.TryGetValue(name, out IVfsFilesystemType? filesystemType))
        {
            return false;
        }

        if (!filesystemType.TryMount(source, flags, out IVfsSuperblock? superblock) || superblock == null)
        {
            return false;
        }

        string normalizedMountPoint = NormalizeMountPoint(mountPoint);
        mount = new VfsMount(name, normalizedMountPoint, filesystemType, superblock);
        s_mounts.Add(mount);

        return true;
    }

    /// <summary>
    /// Retrieve a mount by its mount point.
    /// </summary>
    public static bool TryGetMount(string mountPoint, out VfsMount? mount)
    {
        string normalizedMountPoint = NormalizeMountPoint(mountPoint);

        for (int i = 0; i < s_mounts.Count; i++)
        {
            VfsMount current = s_mounts[i];
            if (string.Equals(current.MountPoint, normalizedMountPoint, StringComparison.Ordinal))
            {
                mount = current;
                return true;
            }
        }

        mount = null;
        return false;
    }

    /// <summary>
    /// Open a file at the given path and return a managed handle wrapper.
    /// </summary>
    public static bool TryOpenFile(string path, out IVfsFileHandle? file)
    {
        file = null;

        if (!TryResolve(path, out IVfsInode? inode, out string? leafName))
        {
            return false;
        }

        IFileOperations? fileOperations = inode.FileOperations;
        if (fileOperations == null)
        {
            return false;
        }

        IVfsOpenFile openFile = new VfsOpenFile(leafName, inode, fileOperations);
        file = new VfsFileHandle(leafName, inode, openFile);
        return true;
    }

    /// <summary>
    /// Open a directory at the given path and return a managed handle wrapper.
    /// </summary>
    public static bool TryOpenDirectory(string path, out IVfsDirectoryHandle? directory)
    {
        directory = null;

        if (!TryResolve(path, out IVfsInode? inode, out string? leafName))
        {
            return false;
        }

        if (inode.InodeOperations == null)
        {
            return false;
        }

        directory = new VfsDirectoryHandle(leafName, inode);
        return true;
    }

    /// <summary>
    /// Wrap an inode into a file or directory handle based on metadata and available operations.
    /// </summary>
    internal static IVfsNodeHandle? WrapNode(string name, IVfsInode inode)
    {
        VfsStat stat;
        if (inode.InodeOperations != null && inode.InodeOperations.GetAttr(inode, out stat))
        {
            ModeEnum type = stat.Mode & ModeEnum.FileTypeMask;
            if (type == ModeEnum.Directory)
            {
                return new VfsDirectoryHandle(name, inode);
            }
        }

        IFileOperations? fileOperations = inode.FileOperations;
        if (fileOperations != null)
        {
            IVfsOpenFile openFile = new VfsOpenFile(name, inode, fileOperations);
            return new VfsFileHandle(name, inode, openFile);
        }

        if (inode.InodeOperations != null)
        {
            return new VfsDirectoryHandle(name, inode);
        }

        return null;
    }

    private static bool TryResolve(string path, out IVfsInode? inode, out string? leafName)
    {
        inode = null;
        leafName = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        VfsMount? mount = FindMount(path);
        if (mount == null)
        {
            return false;
        }

        string relativePath = TrimMountPrefix(mount.MountPoint, path);
        IVfsInode current = mount.Superblock.Root;

        if (relativePath.Length == 0)
        {
            inode = current;
            leafName = mount.MountPoint;
            return true;
        }

        string[] parts = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            string segment = parts[i];
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                return false;
            }

            IInodeOperations operations = current.InodeOperations;
            if (!operations.Lookup(current, segment, out IVfsInode? child) || child == null)
            {
                return false;
            }

            current = child;
            leafName = segment;
        }

        inode = current;
        return true;
    }

    private static VfsMount? FindMount(string path)
    {
        VfsMount? bestMatch = null;

        for (int i = 0; i < s_mounts.Count; i++)
        {
            VfsMount candidate = s_mounts[i];
            if (!path.StartsWith(candidate.MountPoint, StringComparison.Ordinal))
            {
                continue;
            }

            if (bestMatch == null || candidate.MountPoint.Length > bestMatch.MountPoint.Length)
            {
                bestMatch = candidate;
            }
        }

        return bestMatch;
    }

    private static string NormalizeMountPoint(string mountPoint)
    {
        if (string.IsNullOrWhiteSpace(mountPoint))
        {
            return "/";
        }

        string normalized = mountPoint;
        if (!normalized.StartsWith("/", StringComparison.Ordinal))
        {
            normalized = "/" + normalized;
        }

        if (normalized.Length > 1 && normalized.EndsWith("/", StringComparison.Ordinal))
        {
            normalized = normalized.TrimEnd('/');
        }

        return normalized;
    }

    private static string TrimMountPrefix(string mountPoint, string path)
    {
        if (mountPoint == "/")
        {
            return path.TrimStart('/');
        }

        string trimmed = path.StartsWith(mountPoint, StringComparison.Ordinal)
            ? path.Substring(mountPoint.Length)
            : path;

        return trimmed.TrimStart('/');
    }
}
