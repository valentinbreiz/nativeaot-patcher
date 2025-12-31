// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Services.VFS.Interfaces;

namespace Cosmos.Kernel.Services.VFS.FAT;

public class FatFileSystem : IFileSystem
{
    private readonly byte[] _readBuffer;

    public FatFileSystem(Partition partition, string root)
    {
        Partition = partition;
        RootPath = root;
        _readBuffer = partition.BlockDevice.NewBlockArray(1).ToArray();
    }

    public List<IDirectoryEntry> GetDirectoryListing(IDirectoryEntry directoryEntry) => throw new NotImplementedException();

    public IDirectoryEntry? Get(string path)
    {
        string localPath = path[RootPath.Length..];
        string[] parts = localPath.Split('/');
        IDirectoryEntry? current = GetRootDirectory();
        foreach (string part in parts)
        {
            if (current == null) break;
            List<IDirectoryEntry> nodes = GetDirectoryListing(current);
            current = null;
            foreach (IDirectoryEntry node in nodes)
            {
                if (node.Name == part)
                {
                    current = node;
                }
            }
        }

        return current;
    }

    public IDirectoryEntry GetRootDirectory() => throw new NotImplementedException();

    public IDirectoryEntry CreateDirectory(IDirectoryEntry directoryEntry, string aNewDirectory) => throw new NotImplementedException();

    public IDirectoryEntry CreateFile(IDirectoryEntry directoryEntry, string aNewFile) => throw new NotImplementedException();

    public void DeleteDirectory(IDirectoryEntry directoryEntry) => throw new NotImplementedException();

    public void DeleteFile(IDirectoryEntry directoryEntry) => throw new NotImplementedException();

    public Partition Partition { get; }
    public string RootPath { get; }
    public ulong Size { get; }
    public ulong AvailableFreeSpace { get; }
    public string Type { get; }
    public string Label { get; set; }
    public MountFlags Flags { get; set; }
}
