using System;
using System.Collections.Generic;
using Cosmos.Kernel.HAL.Interfaces.Devices;
using Cosmos.Kernel.System.Filesystems.Fat;
using Cosmos.Kernel.System.Storage;
using DevKernel.Shell;

namespace DevKernel.Storage;

/// <summary>
/// Rendering and lookup helpers shared by the disk, partition and filesystem
/// commands, so the labels they print and the numbering they accept cannot
/// drift apart.
/// </summary>
internal static class StorageView
{
    /// <summary>Column width (chars) used to pad the labels of the disk listings.</summary>
    public const int DiskLabelColumnWidth = 17;

    /// <summary>Block count passed to <c>ReadBlock</c> when probing a single sector.</summary>
    private const ulong SingleBlock = 1;

    /// <summary>Reports that storage is off or empty; returns false when there is nothing to list.</summary>
    public static bool RequireDevices()
    {
        if (!StorageManager.IsEnabled)
        {
            Terminal.Error("Storage support is disabled (CosmosEnableStorage=false).");
            return false;
        }

        if (StorageManager.DeviceCount == 0)
        {
            Terminal.Warning("No storage devices discovered. Attach a SATA disk to QEMU and reboot.");
            return false;
        }

        return true;
    }

    /// <summary>Prints the geometry block of every attached device, marking the primary one.</summary>
    public static void PrintDevices(bool detailed)
    {
        Terminal.InfoLine("Device Count", StorageManager.DeviceCount.ToString());

        for (int i = 0; i < StorageManager.DeviceCount; i++)
        {
            IBlockDevice? device = StorageManager.GetDevice(i);
            if (device == null)
            {
                continue;
            }

            Console.WriteLine();
            PrintDeviceBlock(i, device, detailed);
            PrintPrimaryMarker(device);
        }
    }

    /// <summary>Prints one device's name, geometry and partition table type.</summary>
    public static void PrintDeviceBlock(int index, IBlockDevice device, bool detailed)
    {
        ulong totalBytes = device.BlockCount * device.BlockSize;
        Terminal.InfoLine($"[{index}] Name", device.Name, DiskLabelColumnWidth);
        if (detailed)
        {
            Terminal.InfoLine("    Block Size", device.BlockSize.ToString() + " B", DiskLabelColumnWidth);
        }

        Terminal.InfoLine("    Sectors", device.BlockCount.ToString(), DiskLabelColumnWidth);
        Terminal.InfoLine("    Capacity", Units.ToMiB(totalBytes).ToString() + " MiB", DiskLabelColumnWidth);
        Terminal.InfoLine("    Table", DescribePartitionTable(device), DiskLabelColumnWidth);
    }

    /// <summary>Adds the <c>Primary yes</c> line when <paramref name="device"/> is the primary device.</summary>
    public static void PrintPrimaryMarker(IBlockDevice device)
    {
        if (ReferenceEquals(device, StorageManager.PrimaryDevice))
        {
            Terminal.InfoLine("    Primary", "yes", DiskLabelColumnWidth);
        }
    }

    /// <summary>Names the partition table written on <paramref name="device"/>.</summary>
    public static string DescribePartitionTable(IBlockDevice device)
    {
        if (Gpt.IsGpt(device))
        {
            return "GPT";
        }

        if (Mbr.IsMbr(device))
        {
            return "MBR";
        }

        return "None";
    }

    /// <summary>Names the filesystem on <paramref name="partition"/> by parsing its boot sector.</summary>
    public static string DetectFilesystem(Partition partition)
    {
        Span<byte> boot = new byte[partition.BlockSize];
        try
        {
            partition.ReadBlock(FatBootSector.BootSectorLba, SingleBlock, boot);
        }
        catch
        {
            return "unreadable";
        }

        if (FatBootSector.TryParse(boot, out FatBootSector? bootSector) && bootSector != null)
        {
            return bootSector.Type switch
            {
                FatType.Fat12 => "FAT12",
                FatType.Fat16 => "FAT16",
                FatType.Fat32 => "FAT32",
                _ => "FAT"
            };
        }

        return "unknown";
    }

    /// <summary>
    /// Resolves a (disk, per-disk partition) pair to its index in
    /// <see cref="StorageManager.Partitions"/>. That global index is what
    /// VfsManager and PartitionManager expect; per-disk numbering is what the
    /// user sees.
    /// </summary>
    public static bool TryResolvePartition(int diskNumber, int partitionNumber, out int globalIndex, out Partition? partition)
    {
        globalIndex = -1;
        partition = null;

        IBlockDevice? device = StorageManager.GetDevice(diskNumber);
        if (device == null)
        {
            return false;
        }

        int local = 0;
        IReadOnlyList<Partition> all = StorageManager.Partitions;
        for (int i = 0; i < all.Count; i++)
        {
            if (!ReferenceEquals(all[i].Host, device))
            {
                continue;
            }

            if (local == partitionNumber)
            {
                globalIndex = i;
                partition = all[i];
                return true;
            }

            local++;
        }

        return false;
    }

    /// <summary>Inverse of <see cref="TryResolvePartition"/>: maps a global index back to (disk, per-disk partition).</summary>
    public static bool TryResolveGlobalIndex(int globalIndex, out int diskNumber, out int partitionNumber)
    {
        diskNumber = -1;
        partitionNumber = 0;

        IReadOnlyList<Partition> all = StorageManager.Partitions;
        for (int d = 0; d < StorageManager.DeviceCount; d++)
        {
            IBlockDevice? device = StorageManager.GetDevice(d);
            if (device == null)
            {
                continue;
            }

            int local = 0;
            for (int g = 0; g < all.Count; g++)
            {
                if (!ReferenceEquals(all[g].Host, device))
                {
                    continue;
                }

                if (g == globalIndex)
                {
                    diskNumber = d;
                    partitionNumber = local;
                    return true;
                }

                local++;
            }
        }

        return false;
    }
}
