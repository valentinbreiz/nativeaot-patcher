using Cosmos.Kernel.Core;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.Vfs;
using Cosmos.Kernel.System.Filesystems.Fat;
using Cosmos.Kernel.System.Storage;
using Cosmos.Kernel.System.Vfs;

namespace DevKernel.Storage;

/// <summary>
/// Boot-time wiring of the FAT driver: register it with the VFS, then try to
/// mount the first partition so a freshly booted shell already has a filesystem.
/// </summary>
internal static class FatBootstrap
{
    /// <summary>Name the FAT driver is registered under in the VFS.</summary>
    public const string DriverName = "fat";

    /// <summary>Global partition index the boot-time auto-mount tries, as a VfsManager source string.</summary>
    private const string AutoMountSource = "0";

    /// <summary>Mount point used by the boot-time auto-mount.</summary>
    private const string AutoMountPoint = "/mnt";

    /// <summary>Registers the FAT driver and auto-mounts partition 0 when it holds a FAT volume.</summary>
    public static void RegisterAndAutoMount()
    {
        if (!CosmosFeatures.FatEnabled)
        {
            return;
        }

        if (!VfsManager.RegisterFilesystem(DriverName, new FatFilesystemType()))
        {
            Serial.WriteString("[DevKernel] FAT driver already registered or invalid\n");
            return;
        }

        Serial.WriteString("[DevKernel] FAT driver registered\n");

        if (!CosmosFeatures.StorageEnabled || StorageManager.Partitions.Count == 0)
        {
            return;
        }

        if (VfsManager.TryMount(DriverName, AutoMountSource, MountFlags.None, AutoMountPoint, out _))
        {
            Serial.WriteString("[DevKernel] FAT mounted on /mnt from partition 0\n");
        }
        else
        {
            Serial.WriteString("[DevKernel] FAT mount on partition 0 skipped (not FAT or unreadable)\n");
        }
    }
}
