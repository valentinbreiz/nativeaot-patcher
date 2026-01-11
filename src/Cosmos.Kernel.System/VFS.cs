// This code is licensed under MIT license (see LICENSE for details)

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Utilities;
using Cosmos.Kernel.System.FileSystem;

namespace Cosmos.Kernel.System;

/// <summary>
/// Virtual File System implementation, similar to Linux's VFS.
/// Provides a unified interface for file operations across different file systems.
/// </summary>
public static class Vfs
{
    private static readonly SimpleDictionary<FileHandle, IFileHandle> s_openHandles = new();
    private static readonly SimpleDictionary<string, IFileSystemOperations> s_mountPoints = new();
    private static uint s_nextHandleId = 1;

    private static void Log(params object?[] args)
    {
        Serial.WriteString("[VFS] ");
        Serial.Write(args);
        Serial.WriteString("\n");
    }

    private static uint NextHandleId()
    {
        return Interlocked.Increment(ref s_nextHandleId);
    }

    /// <summary>
    /// Opens a file and returns a file handle.
    /// </summary>
    /// <param name="path">The path to the file.</param>
    /// <param name="mode">The file access mode (read, write, read/write).</param>
    /// <param name="create">Whether to create the file if it doesn't exist.</param>
    /// <returns>A file handle, or null if the file cannot be opened.</returns>
    public static FileHandle? Open(string path, FileAccessMode mode, bool create = false)
    {
        Log("Open(", path, ",", mode.AsString(), ",", create, ")");
        if (string.IsNullOrEmpty(path))
            return null;

        // Normalize path
        path = NormalizePath(path);

        // Find the file system for this path
        IFileSystemOperations? fileSystem = GetFileSystem(path);
        if (fileSystem == null)
            return null;

        // Get relative path from mount point
        string relativePath = GetRelativePath(path, fileSystem.MountPoint);

        // Get or create the inode
        IInode? inode = fileSystem.GetInode(relativePath);
        if (inode == null)
        {
            if (create)
            {
                // Find parent directory
                string? parentPath = GetParentPath(relativePath);
                if (parentPath == null)
                    return null;

                IInode? parent = fileSystem.GetInode(parentPath);
                if (parent == null || !parent.IsDirectory)
                    return null;

                string fileName = GetFileName(relativePath);
                inode = fileSystem.InodeOperations.CreateFile(parent, fileName);
            }
            else
            {
                return null;
            }
        }

        if (!inode.IsFile)
            return null;

        // Get file operations
        IFileOperations? fileOps = inode.FileOperations ?? fileSystem.InodeOperations.GetFileOperations(inode);
        if (fileOps == null)
            return null;

        // Create file handle with all file descriptor table data (similar to Linux's struct file)
        FileHandle handle = new FileHandle
        {
            Id = NextHandleId(),
            Position = 0,
            AccessMode = mode,
            Inode = inode,
            FileOperations = fileOps
        };
        // Reference count starts at 0, VfsFileHandle will increment it to 1

        IFileHandle fileHandle = new VfsFileHandle(handle);
        s_openHandles.Add(handle, fileHandle);

        return handle;
    }

    /// <summary>
    /// Closes a file handle.
    /// </summary>
    /// <param name="handle">The file handle to close.</param>
    public static void Close(FileHandle handle)
    {
        Log("Close(", handle.Id, ")");
        if (s_openHandles.TryGetValue(handle, out IFileHandle? fileHandle))
        {
            fileHandle.Close();
            // If reference count reaches 0, remove from the table
            // (Close already decrements the reference count)
            if (handle.ReferenceCount <= 0)
            {
                s_openHandles.Remove(handle);
            }
        }
    }

    /// <summary>
    /// Reads data from a file handle.
    /// </summary>
    /// <param name="handle">The file handle.</param>
    /// <param name="buffer">Buffer to read into.</param>
    /// <param name="offset">Offset in buffer to start writing.</param>
    /// <param name="count">Number of bytes to read.</param>
    /// <returns>Number of bytes read.</returns>
    public static int Read(FileHandle handle, byte[] buffer, int offset, int count)
    {
        Log("Read(", handle.Id, ",", buffer.Length, ",", offset, ",", count, ")");
        if (!s_openHandles.TryGetValue(handle, out IFileHandle? fileHandle))
            throw new ArgumentException("Invalid file handle.", nameof(handle));

        return fileHandle.Read(buffer, offset, count);
    }

    /// <summary>
    /// Writes data to a file handle.
    /// </summary>
    /// <param name="handle">The file handle.</param>
    /// <param name="buffer">Buffer to write from.</param>
    /// <param name="offset">Offset in buffer to start reading.</param>
    /// <param name="count">Number of bytes to write.</param>
    /// <returns>Number of bytes written.</returns>
    public static int Write(FileHandle handle, byte[] buffer, int offset, int count)
    {
        Log("Write(", handle.Id, ",", buffer.Length, ",", offset, ",", count, ")");
        if (!s_openHandles.TryGetValue(handle, out IFileHandle? fileHandle))
            throw new ArgumentException("Invalid file handle.", nameof(handle));

        fileHandle.Write(buffer, offset, count);
        return count;
    }

    /// <summary>
    /// Seeks to a position in a file handle.
    /// </summary>
    /// <param name="handle">The file handle.</param>
    /// <param name="offset">Offset to seek to.</param>
    /// <param name="origin">Origin of the seek operation.</param>
    /// <returns>New position in the file.</returns>
    public static long Seek(FileHandle handle, long offset, SeekOrigin origin)
    {
        Log("Seek(", handle.Id, ",", offset, ",", origin, ")");
        if (!s_openHandles.TryGetValue(handle, out IFileHandle? fileHandle))
            throw new ArgumentException("Invalid file handle.", nameof(handle));

        return fileHandle.Seek(offset, origin);
    }

    /// <summary>
    /// Gets a stream for a file handle.
    /// </summary>
    /// <param name="handle">The file handle.</param>
    /// <returns>A stream for the file handle.</returns>
    public static Stream GetStream(FileHandle handle)
    {
        Log("GetStream(", handle.Id, ")");
        if (!s_openHandles.TryGetValue(handle, out IFileHandle? fileHandle))
            throw new ArgumentException("Invalid file handle.", nameof(handle));

        return new FileHandleStream(fileHandle);
    }

    /// <summary>
    /// Creates a new file.
    /// </summary>
    /// <param name="path">The path where to create the file.</param>
    /// <returns>True if the file was created, false otherwise.</returns>
    public static bool CreateFile(string path)
    {
        Log("CreateFile(", path, ")");
        if (string.IsNullOrEmpty(path))
            return false;

        path = NormalizePath(path);
        IFileSystemOperations? fileSystem = GetFileSystem(path);
        if (fileSystem == null)
            return false;

        string relativePath = GetRelativePath(path, fileSystem.MountPoint);
        string? parentPath = GetParentPath(relativePath);
        if (parentPath == null)
            return false;

        IInode? parent = fileSystem.GetInode(parentPath);
        if (parent == null || !parent.IsDirectory)
            return false;

        string fileName = GetFileName(relativePath);
        try
        {
            fileSystem.InodeOperations.CreateFile(parent, fileName);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Creates a new directory.
    /// </summary>
    /// <param name="path">The path where to create the directory.</param>
    /// <returns>True if the directory was created, false otherwise.</returns>
    public static bool CreateDirectory(string path)
    {
        Log("CreateDirectory(", path, ")");
        if (string.IsNullOrEmpty(path))
            return false;

        path = NormalizePath(path);
        IFileSystemOperations? fileSystem = GetFileSystem(path);
        if (fileSystem == null)
            return false;

        string relativePath = GetRelativePath(path, fileSystem.MountPoint);
        string? parentPath = GetParentPath(relativePath);
        if (parentPath == null)
            return false;

        IInode? parent = fileSystem.GetInode(parentPath);
        if (parent == null || !parent.IsDirectory)
            return false;

        string dirName = GetFileName(relativePath);
        try
        {
            fileSystem.InodeOperations.CreateDirectory(parent, dirName);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Deletes a file or directory.
    /// </summary>
    /// <param name="path">The path to delete.</param>
    /// <returns>True if the file/directory was deleted, false otherwise.</returns>
    public static bool Delete(string path)
    {
        Log("Delete(", path, ")");
        if (string.IsNullOrEmpty(path))
            return false;

        path = NormalizePath(path);
        IFileSystemOperations? fileSystem = GetFileSystem(path);
        if (fileSystem == null)
            return false;

        string relativePath = GetRelativePath(path, fileSystem.MountPoint);
        IInode? inode = fileSystem.GetInode(relativePath);
        if (inode == null)
            return false;

        try
        {
            fileSystem.InodeOperations.Unlink(inode);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Renames a file or directory.
    /// </summary>
    /// <param name="oldPath">The old path.</param>
    /// <param name="newPath">The new path.</param>
    /// <returns>True if the rename was successful, false otherwise.</returns>
    public static bool Rename(string oldPath, string newPath)
    {
        Log("Rename(", oldPath, ",", newPath, ")");
        if (string.IsNullOrEmpty(oldPath) || string.IsNullOrEmpty(newPath))
            return false;

        oldPath = NormalizePath(oldPath);
        newPath = NormalizePath(newPath);

        IFileSystemOperations? oldFileSystem = GetFileSystem(oldPath);
        IFileSystemOperations? newFileSystem = GetFileSystem(newPath);

        // Both paths must be on the same file system
        if (oldFileSystem != newFileSystem || oldFileSystem == null)
            return false;

        string oldRelativePath = GetRelativePath(oldPath, oldFileSystem.MountPoint);
        string newRelativePath = GetRelativePath(newPath, oldFileSystem.MountPoint);

        IInode? oldInode = oldFileSystem.GetInode(oldRelativePath);
        if (oldInode == null)
            return false;

        string? newParentPath = GetParentPath(newRelativePath);
        if (newParentPath == null)
            return false;

        IInode? newParent = oldFileSystem.GetInode(newParentPath);
        if (newParent == null || !newParent.IsDirectory)
            return false;

        string newName = GetFileName(newRelativePath);
        try
        {
            oldFileSystem.InodeOperations.Rename(oldInode, newParent, newName);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if a path exists.
    /// </summary>
    /// <param name="path">The path to check.</param>
    /// <returns>True if the path exists, false otherwise.</returns>
    public static bool Exists(string path)
    {
        Log("Exists(", path, ")");
        if (string.IsNullOrEmpty(path))
            return false;

        path = NormalizePath(path);
        IFileSystemOperations? fileSystem = GetFileSystem(path);
        if (fileSystem == null)
            return false;

        string relativePath = GetRelativePath(path, fileSystem.MountPoint);
        return fileSystem.GetInode(relativePath) != null;
    }

    /// <summary>
    /// Gets information about a file or directory.
    /// </summary>
    /// <param name="path">The path to get information about.</param>
    /// <returns>The inode for the path, or null if not found.</returns>
    public static IInode? GetInode(string path)
    {
        Log("GetInode(", path, ")");
        if (string.IsNullOrEmpty(path))
            return null;

        path = NormalizePath(path);
        IFileSystemOperations? fileSystem = GetFileSystem(path);
        if (fileSystem == null)
            return null;

        string relativePath = GetRelativePath(path, fileSystem.MountPoint);
        return fileSystem.GetInode(relativePath);
    }

    /// <summary>
    /// Lists the contents of a directory.
    /// </summary>
    /// <param name="path">The path to the directory.</param>
    /// <returns>List of inodes in the directory, or an empty list if the path is not a directory or doesn't exist.</returns>
    public static List<IInode> ListDirectory(string path)
    {
        Log("ListDirectory(", path, ")");
        if (string.IsNullOrEmpty(path))
            return new List<IInode>();

        path = NormalizePath(path);
        IFileSystemOperations? fileSystem = GetFileSystem(path);
        if (fileSystem == null)
        {
            Log("ListDirectory(", path, ") No FileSystem Found");
            return new List<IInode>();
        }


        string relativePath = GetRelativePath(path, fileSystem.MountPoint);
        IInode? inode = fileSystem.GetInode(relativePath);
        if (inode == null || !inode.IsDirectory)
            return new List<IInode>();

        return fileSystem.InodeOperations.ReadDirectory(inode);
    }

    /// <summary>
    /// Mounts a file system at a mount point.
    /// </summary>
    /// <param name="fileSystem">The file system to mount.</param>
    /// <param name="mountPoint">The mount point path.</param>
    public static void Mount(IFileSystemOperations fileSystem, string mountPoint)
    {
        Log("Mount( dummy ,", mountPoint, ")");
        if (fileSystem == null)
            throw new ArgumentNullException(nameof(fileSystem));
        if (string.IsNullOrEmpty(mountPoint))
            throw new ArgumentException("Mount point cannot be null or empty.", nameof(mountPoint));

        mountPoint = NormalizePath(mountPoint);
        Log("Mount( dummy ,", mountPoint, ")", "Mounting ", mountPoint);
        s_mountPoints.Add(mountPoint,  fileSystem);
    }

    /// <summary>
    /// Unmounts a file system from a mount point.
    /// </summary>
    /// <param name="mountPoint">The mount point path.</param>
    public static void Unmount(string mountPoint)
    {
        Log("Unmount(", mountPoint, ")");
        if (string.IsNullOrEmpty(mountPoint))
            return;

        mountPoint = NormalizePath(mountPoint);
        s_mountPoints.Remove(mountPoint);
    }

    // Helper methods

    private static IFileSystemOperations? GetFileSystem(string path)
    {
        Log("GetFileSystem(", path, ")");
        string bestMatch = "/";
        Log("s_mountPoints.Keys ", s_mountPoints.Keys.Count, " s_mountPoints.Count ", s_mountPoints.Count);
        foreach (string mountPoint in s_mountPoints.Keys)
        {
            if (mountPoint == path)
            {
                return s_mountPoints[mountPoint];
            }

            if (path.StartsWith(mountPoint))
            {
                if (mountPoint.Length > bestMatch.Length)
                {
                    bestMatch = mountPoint;
                }
            }
        }
        Log("GetFileSystem(", path, ") bestMatch:", bestMatch);
        if (bestMatch == string.Empty)
            return null;

        if (s_mountPoints.TryGetValue(bestMatch, out IFileSystemOperations? fileSystem))
        {
            Log("GetFileSystem(", path, ") found FS");
            return fileSystem;
        }
        else
        {
            Log("GetFileSystem(", path, ") could not find FS");
            return null;
        }

    }

    private static string NormalizePath(string path)
    {
        Log("NormalizePath(", path, ")");
        // Remove trailing slashes (except for root)
        path = path.TrimEnd('/');
        if (string.IsNullOrEmpty(path))
            return "/";

        // Ensure leading slash
        if (!path.StartsWith('/'))
            path = "/" + path;

        // Remove duplicate slashes
        while (path.Contains("//"))
            path = path.Replace("//", "/");

        return path;
    }

    private static string GetRelativePath(string path, string mountPoint)
    {
        Log("GetRelativePath(", path, ",", mountPoint , ")");
        if (path == mountPoint)
            return "/";

        if (path.StartsWith(mountPoint + "/"))
            return path.Substring(mountPoint.Length);

        return path;
    }

    private static string? GetParentPath(string path)
    {
        Log("GetParentPath(", path, ")");
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
        Log("GetFileName(", path, ")");
        if (path == "/")
            return "/";

        int lastSlash = path.LastIndexOf('/');
        if (lastSlash >= 0 && lastSlash < path.Length - 1)
            return path.Substring(lastSlash + 1);

        return path;
    }
}
