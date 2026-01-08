// This code is licensed under MIT license (see LICENSE for details)

using System.Diagnostics.CodeAnalysis;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.System.VFS.Enums;
using Cosmos.Kernel.System.VFS.FAT;
using Cosmos.Kernel.System.VFS.Interfaces;

namespace Cosmos.Kernel.System.VFS;

public static class VfsManager
{
    private static readonly IIsFileSystem[] s_systemCheckers = [new FatIsFileSystem()];

    // Now using Dictionary since GetHashCode is fixed
    private static readonly Dictionary<string, IFileSystem> s_mountedFileSystems = new();

    private static IFileSystem? GetFileSystemForPath(string path)
    {
        IFileSystem? bestMatch = null;
        int bestMatchLen = 0;

        foreach (var kvp in s_mountedFileSystems)
        {
            if (path.StartsWith(kvp.Key))
            {
                if (kvp.Key.Length > bestMatchLen)
                {
                    bestMatchLen = kvp.Key.Length;
                    bestMatch = kvp.Value;
                }
            }
        }

        return bestMatch;
    }

    public static IIsFileSystem? GetFileSystem(Partition partition)
    {
        foreach (IIsFileSystem fs in s_systemCheckers)
        {
            if (fs.IsFormat(partition)) return fs;
        }
        return null;
    }

    public static void Mount(Partition partition, string mountPoint, MountFlags flags = MountFlags.Default)
    {
        Serial.WriteString("[VfsManager] Mount called for: ");
        Serial.WriteString(mountPoint);
        Serial.WriteString("\n");

        IIsFileSystem? fs = GetFileSystem(partition);
        if (fs == null)
        {
            Serial.WriteString("[VfsManager] ERROR: No filesystem detected\n");
            throw new NotSupportedException();
        }

        Serial.WriteString("[VfsManager] Getting filesystem...\n");
        var fileSystem = fs.GetFileSystem(partition, mountPoint);
        Serial.WriteString("[VfsManager] Filesystem created, mounting...\n");
        Mount(fileSystem, flags);
        Serial.WriteString("[VfsManager] Mount complete\n");
    }

    public static void Mount(IFileSystem fileSystem, MountFlags flags = MountFlags.Default)
    {
        Serial.WriteString("[VfsManager] Mount(IFileSystem) - RootPath: ");
        Serial.WriteString(fileSystem.RootPath);
        Serial.WriteString("\n");

        fileSystem.Flags = flags;

        Serial.WriteString("[VfsManager] Adding to dictionary...\n");
        s_mountedFileSystems[fileSystem.RootPath] = fileSystem;
        Serial.WriteString("[VfsManager] Added. Count: ");
        Serial.WriteNumber((ulong)s_mountedFileSystems.Count);
        Serial.WriteString("\n");
    }

    public static void Unmount(string mountPoint)
    {
        s_mountedFileSystems.Remove(mountPoint);
    }

    public static int GetMountCount() => s_mountedFileSystems.Count;

    public static (string Path, IFileSystem FileSystem)? GetMountAt(int index)
    {
        int i = 0;
        foreach (var kvp in s_mountedFileSystems)
        {
            if (i == index)
                return (kvp.Key, kvp.Value);
            i++;
        }
        return null;
    }

    public static bool IsMounted(string mountPoint)
    {
        return s_mountedFileSystems.ContainsKey(mountPoint);
    }

    #region File Operations

    /// <summary>
    /// Creates a new file at the specified path.
    /// </summary>
    public static IDirectoryEntry? CreateFile(string path)
    {
        Serial.WriteString("[VFS] CreateFile: ");
        Serial.WriteString(path);
        Serial.WriteString("\n");

        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentNullException(nameof(path));
        }

        IFileSystem? fs = GetFileSystemForPath(path);
        if (fs == null)
        {
            throw new Exception("Unable to determine filesystem for path: " + path);
        }

        // Get parent directory
        string parentPath = GetParentPath(path);
        string fileName = GetFileName(path);

        IDirectoryEntry? parent = fs.Get(parentPath);
        if (parent == null)
        {
            throw new Exception("Parent directory not found: " + parentPath);
        }

        return fs.CreateFile(parent, fileName);
    }

    /// <summary>
    /// Deletes a file at the specified path.
    /// </summary>
    public static void DeleteFile(string path)
    {
        Serial.WriteString("[VFS] DeleteFile: ");
        Serial.WriteString(path);
        Serial.WriteString("\n");

        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentNullException(nameof(path));
        }

        IFileSystem? fs = GetFileSystemForPath(path);
        if (fs == null)
        {
            throw new Exception("Unable to determine filesystem for path: " + path);
        }

        IDirectoryEntry? entry = fs.Get(path);
        if (entry == null)
        {
            throw new Exception("File not found: " + path);
        }

        if (entry.Type != DirectoryEntryType.File)
        {
            throw new Exception("Path is not a file: " + path);
        }

        fs.DeleteFile(entry);
    }

    /// <summary>
    /// Creates a new directory at the specified path.
    /// </summary>
    public static IDirectoryEntry? CreateDirectory(string path)
    {
        Serial.WriteString("[VFS] CreateDirectory: ");
        Serial.WriteString(path);
        Serial.WriteString("\n");

        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentNullException(nameof(path));
        }

        IFileSystem? fs = GetFileSystemForPath(path);
        if (fs == null)
        {
            throw new Exception("Unable to determine filesystem for path: " + path);
        }

        // Get parent directory
        string parentPath = GetParentPath(path);
        string dirName = GetFileName(path);

        IDirectoryEntry? parent = fs.Get(parentPath);
        if (parent == null)
        {
            throw new Exception("Parent directory not found: " + parentPath);
        }

        return fs.CreateDirectory(parent, dirName);
    }

    /// <summary>
    /// Deletes a directory at the specified path.
    /// </summary>
    public static void DeleteDirectory(string path, bool recursive = false)
    {
        Serial.WriteString("[VFS] DeleteDirectory: ");
        Serial.WriteString(path);
        Serial.WriteString("\n");

        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentNullException(nameof(path));
        }

        IFileSystem? fs = GetFileSystemForPath(path);
        if (fs == null)
        {
            throw new Exception("Unable to determine filesystem for path: " + path);
        }

        IDirectoryEntry? entry = fs.Get(path);
        if (entry == null)
        {
            throw new Exception("Directory not found: " + path);
        }

        if (entry.Type != DirectoryEntryType.Directory)
        {
            throw new Exception("Path is not a directory: " + path);
        }

        // Check if empty or recursive
        var contents = fs.GetDirectoryListing(entry);
        if (contents.Count > 0 && !recursive)
        {
            throw new Exception("Directory is not empty");
        }

        if (recursive)
        {
            foreach (var child in contents)
            {
                if (child.Type == DirectoryEntryType.Directory)
                {
                    DeleteDirectory(child.Path, true);
                }
                else
                {
                    DeleteFile(child.Path);
                }
            }
        }

        fs.DeleteDirectory(entry);
    }

    /// <summary>
    /// Gets the directory listing for the specified path.
    /// </summary>
    public static List<IDirectoryEntry> GetDirectoryListing(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentNullException(nameof(path));
        }

        IFileSystem? fs = GetFileSystemForPath(path);
        if (fs == null)
        {
            throw new Exception("Unable to determine filesystem for path: " + path);
        }

        IDirectoryEntry? entry = fs.Get(path);
        if (entry == null)
        {
            throw new Exception("Path not found: " + path);
        }

        return fs.GetDirectoryListing(entry);
    }

    /// <summary>
    /// Gets a directory entry at the specified path.
    /// </summary>
    public static IDirectoryEntry? GetEntry(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        IFileSystem? fs = GetFileSystemForPath(path);
        return fs?.Get(path);
    }

    /// <summary>
    /// Checks if a file exists at the specified path.
    /// </summary>
    public static bool FileExists(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        try
        {
            IDirectoryEntry? entry = GetEntry(path);
            return entry != null && entry.Type == DirectoryEntryType.File;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if a directory exists at the specified path.
    /// </summary>
    public static bool DirectoryExists(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        try
        {
            IDirectoryEntry? entry = GetEntry(path);
            return entry != null && entry.Type == DirectoryEntryType.Directory;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region Stream Operations

    /// <summary>
    /// Gets a stream for the file at the specified path.
    /// </summary>
    [SuppressMessage("ReSharper", "UseNullPropagation")]
    public static Stream? GetStream(string path)
    {
        IFileSystem? fs = GetFileSystemForPath(path);
        if (fs == null)
        {
            return null;
        }

        IDirectoryEntry? node = fs.Get(path);
        if (node == null)
        {
            return null;
        }

        if (node is IFileEntry file)
        {
            return file.GetFileStream();
        }

        // For FatDirectoryEntry that isn't IFileEntry but is a file
        if (node is FatDirectoryEntry fatEntry && node.Type == DirectoryEntryType.File)
        {
            return new FatFileStream(fatEntry.FileSystem, fatEntry);
        }

        return null;
    }

    /// <summary>
    /// Reads all bytes from a file.
    /// </summary>
    public static byte[]? ReadAllBytes(string path)
    {
        using var stream = GetStream(path);
        if (stream == null)
        {
            return null;
        }

        byte[] buffer = new byte[stream.Length];
        stream.Read(buffer, 0, buffer.Length);
        return buffer;
    }

    /// <summary>
    /// Reads all text from a file.
    /// </summary>
    public static string? ReadAllText(string path)
    {
        byte[]? bytes = ReadAllBytes(path);
        if (bytes == null)
        {
            return null;
        }

        return global::System.Text.Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// Writes all bytes to a file, creating it if it doesn't exist.
    /// </summary>
    public static void WriteAllBytes(string path, byte[] data)
    {
        if (!FileExists(path))
        {
            CreateFile(path);
        }

        using var stream = GetStream(path);
        if (stream == null)
        {
            throw new Exception("Failed to open file for writing: " + path);
        }

        stream.SetLength(data.Length);
        stream.Position = 0;
        stream.Write(data, 0, data.Length);
    }

    /// <summary>
    /// Writes all text to a file, creating it if it doesn't exist.
    /// </summary>
    public static void WriteAllText(string path, string text)
    {
        byte[] data = global::System.Text.Encoding.UTF8.GetBytes(text);
        WriteAllBytes(path, data);
    }

    #endregion

    #region Helper Methods

    private static string GetParentPath(string path)
    {
        // Normalize separators
        path = path.Replace('\\', '/');

        // Remove trailing slash
        if (path.EndsWith('/') && path.Length > 1)
        {
            path = path[..^1];
        }

        int lastSlash = path.LastIndexOf('/');
        if (lastSlash <= 0)
        {
            // Return root
            int colonIdx = path.IndexOf(':');
            if (colonIdx > 0)
            {
                return path[..(colonIdx + 2)]; // Include "X:/"
            }
            return "/";
        }

        return path[..lastSlash];
    }

    private static string GetFileName(string path)
    {
        // Normalize separators
        path = path.Replace('\\', '/');

        // Remove trailing slash
        if (path.EndsWith('/') && path.Length > 1)
        {
            path = path[..^1];
        }

        int lastSlash = path.LastIndexOf('/');
        if (lastSlash < 0)
        {
            return path;
        }

        return path[(lastSlash + 1)..];
    }

    #endregion
}
