// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.System.FileSystem;

namespace Cosmos.Kernel.System.FileSystem.RootFs;

/// <summary>
/// Root file system implementation, similar to Linux's rootfs.
/// Provides the base directory structure and manages the root mount point.
/// </summary>
public static class RootFs
{
    /// <summary>
    /// Standard Linux root directory structure.
    /// </summary>
    private static readonly string[] s_standardDirectories = new[]
    {
        "/bin",      // Essential user binaries
        "/sbin",     // Essential system binaries
        "/boot",     // Boot loader files
        "/dev",      // Device files
        "/etc",      // Configuration files
        "/home",     // User home directories
        "/lib",      // Essential shared libraries
        "/lib64",    // 64-bit shared libraries
        "/media",    // Removable media mount points
        "/mnt",      // Temporary mount points
        "/opt",      // Optional application software
        "/proc",     // Process information (virtual)
        "/root",     // Root user home directory
        "/run",      // Runtime variable data
        "/srv",      // Service data
        "/sys",      // System information (virtual)
        "/tmp",      // Temporary files
        "/usr",      // User programs and data
        "/var",      // Variable data files
        "/var/log",  // Log files
        "/var/tmp",  // Temporary files in /var
    };

    /// <summary>
    /// Creates and initializes the root file system.
    /// </summary>
    /// <returns>The root file system operations interface.</returns>
    public static IFileSystemOperations Create()
    {
        // Create a MemFs instance as the underlying storage for rootfs
        IFileSystemOperations rootFs = MemFs.MemFs.Create("/");

        // Initialize standard Linux directory structure
        InitializeDirectoryStructure(rootFs);

        return rootFs;
    }

    /// <summary>
    /// Initializes the standard Linux directory structure.
    /// </summary>
    private static void InitializeDirectoryStructure(IFileSystemOperations rootFs)
    {
        foreach (string dirPath in s_standardDirectories)
        {
            try
            {
                // Create directory if it doesn't exist
                string? parentPath = GetParentPath(dirPath);
                if (parentPath == null)
                    continue;

                IInode? parent = rootFs.GetInode(parentPath);
                if (parent == null || !parent.IsDirectory)
                    continue;

                string dirName = GetFileName(dirPath);

                // Check if directory already exists
                IInode? existing = rootFs.InodeOperations.Lookup(parent, dirName);
                if (existing == null)
                {
                    rootFs.InodeOperations.CreateDirectory(parent, dirName);
                }
            }
            catch
            {
                // Ignore errors during initialization (directory might already exist)
            }
        }
    }

    /// <summary>
    /// Gets the parent path of a given path.
    /// </summary>
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

    /// <summary>
    /// Gets the file name from a path.
    /// </summary>
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
