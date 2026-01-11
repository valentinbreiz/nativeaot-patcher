// This code is licensed under MIT license (see LICENSE for details)

using System;

namespace Cosmos.Kernel.System.FileSystem.MemFs;

/// <summary>
/// In-memory file system implementation with symlink support.
/// </summary>
public static class MemFs
{
    /// <summary>
    /// Creates a new in-memory file system and mounts it at the specified mount point.
    /// </summary>
    /// <param name="mountPoint">The mount point path (e.g., "/memfs").</param>
    /// <returns>The file system operations interface.</returns>
    public static IFileSystemOperations Create(string mountPoint = "/memfs")
    {
        return new MemFsFileSystemOperations(mountPoint);
    }

    /// <summary>
    /// Creates a symlink in the file system.
    /// </summary>
    /// <param name="fileSystem">The file system operations.</param>
    /// <param name="linkPath">The path where to create the symlink.</param>
    /// <param name="targetPath">The target path the symlink points to.</param>
    /// <returns>True if the symlink was created, false otherwise.</returns>
    public static bool CreateSymlink(IFileSystemOperations fileSystem, string linkPath, string targetPath)
    {
        if (fileSystem is not MemFsFileSystemOperations memFs)
            return false;

        if (string.IsNullOrEmpty(linkPath) || string.IsNullOrEmpty(targetPath))
            return false;

        // Normalize link path
        linkPath = NormalizePath(linkPath);

        // Get parent directory
        string? parentPath = GetParentPath(linkPath);
        if (parentPath == null)
            return false;

        IInode? parent = fileSystem.GetInode(parentPath);
        if (parent == null || !parent.IsDirectory)
            return false;

        string linkName = GetFileName(linkPath);

        try
        {
            if (memFs.InodeOperations is MemFsInodeOperations memOps)
            {
                memOps.CreateSymlink(parent, linkName, targetPath);
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
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

    private static string? GetParentPath(string path)
    {
        if (path == "/")
            return null;

        int lastSlash = path.LastIndexOf('/');
        if (lastSlash == 0)
            return "/";

        if (lastSlash > 0)
            return path.Substring(0, lastSlash);

        return null;
    }

    private static string GetFileName(string path)
    {
        if (path == "/")
            return "/";

        int lastSlash = path.LastIndexOf('/');
        if (lastSlash >= 0 && lastSlash < path.Length - 1)
            return path.Substring(lastSlash + 1);

        return path;
    }
}
