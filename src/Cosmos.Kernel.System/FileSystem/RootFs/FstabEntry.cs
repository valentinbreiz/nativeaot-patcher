// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.System.FileSystem.RootFs;

/// <summary>
/// Represents an entry in /etc/fstab (file system table).
/// Based on Linux's fstab format.
/// </summary>
public class FstabEntry
{
    /// <summary>
    /// The device or file system to mount (e.g., /dev/sda1, UUID=..., LABEL=...).
    /// </summary>
    public string Device { get; set; } = string.Empty;

    /// <summary>
    /// The mount point directory.
    /// </summary>
    public string MountPoint { get; set; } = string.Empty;

    /// <summary>
    /// The file system type (e.g., ext4, vfat, tmpfs, proc, sysfs).
    /// </summary>
    public string FileSystemType { get; set; } = string.Empty;

    /// <summary>
    /// Mount options (comma-separated, e.g., "rw,noatime,user").
    /// </summary>
    public string Options { get; set; } = "defaults";

    /// <summary>
    /// Dump flag (0 = don't dump, 1 = dump).
    /// </summary>
    public int Dump { get; set; } = 0;

    /// <summary>
    /// Pass number for fsck (0 = don't check, 1 = check first, 2 = check second).
    /// </summary>
    public int Pass { get; set; } = 0;

    /// <summary>
    /// Whether this entry should be mounted at boot.
    /// </summary>
    public bool MountAtBoot => FileSystemType != "noauto" && !Options.Contains("noauto");

    /// <summary>
    /// Whether this entry is commented out (starts with #).
    /// </summary>
    public bool IsCommented { get; set; } = false;

    /// <summary>
    /// Creates a string representation of the fstab entry.
    /// </summary>
    public override string ToString()
    {
        if (IsCommented)
            return $"# {Device} {MountPoint} {FileSystemType} {Options} {Dump} {Pass}";
        return $"{Device} {MountPoint} {FileSystemType} {Options} {Dump} {Pass}";
    }
}
