using System;
using System.Collections.Generic;
using Cosmos.Kernel.HAL.Vfs;
using Cosmos.Kernel.System.Filesystems.Fat;
using Cosmos.Kernel.System.Storage;
using Cosmos.Kernel.System.Vfs;
using DevKernel.Shell;
using DevKernel.Storage;

namespace DevKernel.Commands;

/// <summary>
/// Putting a filesystem on a partition and attaching it to the VFS tree.
/// </summary>
internal static class MountCommands
{
    /// <summary>Help section these commands are listed under.</summary>
    private const string Category = "Filesystem";

    /// <summary>Filesystem type assumed when <c>format</c> is given no explicit one.</summary>
    private const string DefaultFsType = "fat";

    public static void Register(CommandShell shell)
    {
        shell.Register(
            Category,
            new ShellCommand
            {
                Name = "format",
                Usage = "format <disk> <part> [fs_type]",
                Description = "Format a partition (fs: fat | fat12 | fat16 | fat32, default fat)",
                MinArgs = 2,
                MaxArgs = 3,
                Execute = static (context, args) =>
                {
                    if (!args.TryGetInt(0, out int diskNumber) || !args.TryGetInt(1, out int partitionNumber))
                    {
                        args.PrintUsage();
                        return;
                    }

                    FormatPartition(diskNumber, partitionNumber, args.Count >= 3 ? args.GetLower(2) : DefaultFsType);
                },
            },
            new ShellCommand
            {
                Name = "mount",
                Usage = "mount <disk> <part> <mountpoint>",
                Description = "Mount a partition at <mountpoint> (e.g. mount 0 0 /mnt)",
                MinArgs = 3,
                MaxArgs = 3,
                Execute = static (context, args) =>
                {
                    if (!args.TryGetInt(0, out int diskNumber) || !args.TryGetInt(1, out int partitionNumber))
                    {
                        args.PrintUsage();
                        return;
                    }

                    MountPartition(diskNumber, partitionNumber, args[2]);
                },
            },
            new ShellCommand
            {
                Name = "mounts",
                Usage = "mounts",
                Description = "Show mounted filesystems",
                Execute = static (context, args) => ShowMountPoints(),
            });
    }

    /// <summary>Maps a <c>fat*</c> type name onto the format hint the FAT driver expects.</summary>
    private static bool TryResolveFsType(string fsType, out IVfsFormatOptions? options)
    {
        options = null;
        switch (fsType)
        {
            case DefaultFsType:
                return true;
            case "fat12":
                options = new FatFormatOptions { Type = FatType.Fat12 };
                return true;
            case "fat16":
                options = new FatFormatOptions { Type = FatType.Fat16 };
                return true;
            case "fat32":
                options = new FatFormatOptions { Type = FatType.Fat32 };
                return true;
            default:
                Terminal.Error("Unknown filesystem: " + fsType + ". Supported: fat, fat12, fat16, fat32.");
                return false;
        }
    }

    private static void FormatPartition(int diskNumber, int partitionNumber, string fsType)
    {
        if (!StorageView.TryResolvePartition(diskNumber, partitionNumber, out int globalIndex, out Partition? target) || target == null)
        {
            Terminal.Error("Invalid disk/partition. Use 'lspart' to list.");
            return;
        }

        if (!TryResolveFsType(fsType, out IVfsFormatOptions? options))
        {
            return;
        }

        // Refuse formatting a mounted partition: there is no umount command, so
        // the stale superblock would flush cached FAT and directory state with
        // the old geometry over the fresh volume. mount.Source is the global
        // partition index recorded at mount time.
        for (int i = 0; i < VfsManager.Mounts.Count; i++)
        {
            VfsManager.VfsMount mount = VfsManager.Mounts[i];
            if (int.TryParse(mount.Source, out int mountedIndex) && mountedIndex == globalIndex)
            {
                Terminal.Error("Partition is mounted at " + mount.MountPoint + ". Reboot before reformatting.");
                return;
            }
        }

        if (!VfsManager.TryFormat(FatBootstrap.DriverName, globalIndex.ToString(), options))
        {
            ulong sizeMiB = Units.ToMiB(target.BlockCount * target.BlockSize);
            Terminal.Error("Format failed: partition is likely too small for " + fsType.ToUpper() +
                " (" + sizeMiB + " MiB). Try 'format " + diskNumber + " " + partitionNumber + " fat' to auto-pick a variant.");
            return;
        }

        Terminal.Success("Disk " + diskNumber + " partition " + partitionNumber + " formatted as " + fsType.ToUpper() + ".");
    }

    private static void MountPartition(int diskNumber, int partitionNumber, string mountPoint)
    {
        if (!StorageView.TryResolvePartition(diskNumber, partitionNumber, out int globalIndex, out Partition? _))
        {
            Terminal.Error("Invalid disk/partition. Use 'lspart' to list.");
            return;
        }

        if (string.IsNullOrEmpty(mountPoint) || mountPoint[0] != VfsPath.Separator)
        {
            Terminal.Error("Mount point must be an absolute path (e.g. /mnt).");
            return;
        }

        if (!VfsManager.TryMount(FatBootstrap.DriverName, globalIndex.ToString(), MountFlags.None, mountPoint, out _))
        {
            Terminal.Error("Mount failed (not FAT or unreadable).");
            return;
        }

        Terminal.Success("Disk " + diskNumber + " partition " + partitionNumber + " mounted at " + mountPoint);
    }

    private static void ShowMountPoints()
    {
        Terminal.Header("Mounted Filesystems:");

        IReadOnlyList<VfsManager.VfsMount> mounts = VfsManager.Mounts;
        if (mounts.Count == 0)
        {
            Terminal.Warning("No filesystems mounted.");
            return;
        }

        for (int i = 0; i < mounts.Count; i++)
        {
            VfsManager.VfsMount mount = mounts[i];
            Console.Write("  ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write(mount.MountPoint);
            Console.ResetColor();
            Console.Write(" -> ");

            PrintMountSource(mount);

            if (mount.Superblock.SuperOperations.StatFs(mount.Superblock, out VfsStatFs stats))
            {
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.Write(" (" + Units.ToMiB(stats.Blocks * stats.BlockSize) + " MiB)");
                Console.ResetColor();
            }

            Console.WriteLine();
        }
    }

    /// <summary>
    /// For the FAT driver, <c>mount.Source</c> is the index into
    /// <see cref="StorageManager.Partitions"/> <em>recorded at mount time</em>.
    /// mkpart/rmpart/format rescans shift those indices, so a stale mapping must
    /// not be presented as authoritative.
    /// </summary>
    private static void PrintMountSource(VfsManager.VfsMount mount)
    {
        if (!int.TryParse(mount.Source, out int globalIndex)
            || globalIndex < 0
            || globalIndex >= StorageManager.Partitions.Count)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(mount.Name);
            Console.ResetColor();
            return;
        }

        Partition partition = StorageManager.Partitions[globalIndex];
        StorageView.TryResolveGlobalIndex(globalIndex, out int diskNumber, out int partitionNumber);

        Console.ForegroundColor = ConsoleColor.White;
        Console.Write("disk " + diskNumber + " part " + partitionNumber);
        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write("  " + partition.Name + " (index at mount time)");
        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.Write("  " + StorageView.DetectFilesystem(partition));
        Console.ResetColor();
    }
}
