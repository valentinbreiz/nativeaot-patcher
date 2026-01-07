// This code is licensed under MIT license (see LICENSE for details)

using System.Diagnostics.CodeAnalysis;
using Cosmos.Kernel.System.VFS.FAT;
using Cosmos.Kernel.System.VFS.Interfaces;

namespace Cosmos.Kernel.System.VFS;

public static class VfsManager
{

    private static readonly IIsFileSystem[] s_systemCheckers = [new FatIsFileSystem()];

    private static readonly Dictionary<string, IFileSystem> s_mountPoints = new Dictionary<string, IFileSystem>();

    private static IFileSystem? GetFileSystem(string path)
    {
        string bestMatch = string.Empty;
        foreach (string mountPoint in s_mountPoints.Keys)
        {
            if (path.StartsWith(mountPoint))
            {
                if (mountPoint.Length > bestMatch.Length)
                {
                    bestMatch = mountPoint;
                }
            }
        }

        if (bestMatch == string.Empty)
        {
            return null;
        }
        return s_mountPoints[bestMatch];
    }

    public static IIsFileSystem? GetFileSystem(Partition partition)
    {
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (IIsFileSystem fs in s_systemCheckers)
        {
            if (fs.IsFormat(partition)) return fs;
        }
        return null;
    }

    public static void Mount(Partition partition, string mountPoint, MountFlags flags = MountFlags.Default)
    {
        IIsFileSystem? fs = GetFileSystem(partition) ?? throw new NotSupportedException();
        Mount(fs.GetFileSystem(partition, mountPoint), flags);
    }

    public static void Mount(IFileSystem fileSystem, MountFlags flags = MountFlags.Default)
    {
        fileSystem.Flags = flags;
        s_mountPoints.Add(fileSystem.RootPath, fileSystem);
    }

    [SuppressMessage("ReSharper", "UseNullPropagation")]
    public static Stream? GetStream(string path)
    {
        IFileSystem? fs = GetFileSystem(path);
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

        return null;

    }

}
