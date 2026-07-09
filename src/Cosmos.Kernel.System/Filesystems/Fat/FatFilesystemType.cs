// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.HAL.Interfaces.Devices;
using Cosmos.Kernel.HAL.Vfs;
using Cosmos.Kernel.System.Storage;

namespace Cosmos.Kernel.System.Filesystems.Fat;

/// <summary>
/// FAT12 / FAT16 / FAT32 driver entry point. Pluggable into the VFS via
/// <c>VfsManager.RegisterFilesystem</c>. Source strings:
/// <list type="bullet">
///   <item><description>Empty: use the injected device passed to the constructor (test seam).</description></item>
///   <item><description><c>"&lt;index&gt;"</c>: partition index in <see cref="StorageManager.Partitions"/>.</description></item>
/// </list>
/// </summary>
public sealed class FatFilesystemType : IVfsFilesystemType
{
    private readonly IBlockDevice? _injectedDevice;

    public FatFilesystemType()
    {
    }

    public FatFilesystemType(IBlockDevice device)
    {
        _injectedDevice = device;
    }

    public bool TryMount(ReadOnlySpan<char> source, MountFlags flags, out IVfsSuperblock? superblock)
    {
        superblock = null;

        IBlockDevice? device = ResolveDevice(source);
        if (device == null)
        {
            return false;
        }

        Span<byte> bpb = new byte[device.BlockSize];
        device.ReadBlock(0, 1, bpb);

        if (!FatBootSector.TryParse(bpb, out FatBootSector? boot) || boot == null)
        {
            return false;
        }

        if (boot.BytesPerSector != device.BlockSize)
        {
            return false;
        }
        // The parser never sees the device: reject volumes whose claimed
        // geometry leaves it, and FAT32 root clusters outside the data
        // area (0 underflows ClusterToLba's cluster - 2), before any
        // FAT or cluster I/O can hit the device's range guard.
        if (boot.TotalSectorCount > device.BlockCount)
        {
            return false;
        }
        if (boot.Type == FatType.Fat32
            && (boot.RootCluster < 2 || boot.RootCluster >= boot.ClusterCount + 2))
        {
            return false;
        }

        superblock = new FatSuperblock(device, boot);
        return true;
    }

    public bool TryFormat(ReadOnlySpan<char> source, IVfsFormatOptions? options)
    {
        IBlockDevice? device = ResolveDevice(source);
        if (device == null)
        {
            return false;
        }

        FatFormatOptions? fatOptions = options as FatFormatOptions;
        if (options != null && fatOptions == null)
        {
            return false;
        }

        return FatFormatter.Format(device, fatOptions);
    }

    public bool TryDestroy(ReadOnlySpan<char> source)
    {
        IBlockDevice? device = ResolveDevice(source);
        if (device == null)
        {
            return false;
        }
        return FatFormatter.Destroy(device);
    }

    private IBlockDevice? ResolveDevice(ReadOnlySpan<char> source)
    {
        if (source.IsEmpty || source.IsWhiteSpace())
        {
            return _injectedDevice;
        }

        if (!TryParseInt(source, out int partitionIndex))
        {
            return null;
        }

        IReadOnlyList<Partition> partitions = StorageManager.Partitions;
        if (partitionIndex < 0 || partitionIndex >= partitions.Count)
        {
            return null;
        }

        return partitions[partitionIndex];
    }

    private static bool TryParseInt(ReadOnlySpan<char> source, out int value)
    {
        value = 0;
        if (source.Length == 0)
        {
            return false;
        }

        int result = 0;
        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            if (c < '0' || c > '9')
            {
                return false;
            }
            result = result * 10 + (c - '0');
        }

        value = result;
        return true;
    }
}
