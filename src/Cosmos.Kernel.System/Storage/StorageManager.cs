// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.Devices.Storage;
using Cosmos.Kernel.HAL.Interfaces.Devices;
using Cosmos.Kernel.System.Filesystems.Fat;
using SchedSpinLock = Cosmos.Kernel.Core.Scheduler.SpinLock;

namespace Cosmos.Kernel.System.Storage;

/// <summary>
/// Manages block storage devices.
/// </summary>
public static class StorageManager
{
    /// <summary>Maximum number of block devices the manager can register.</summary>
    private const int MaxDevices = 8;

    /// <summary>
    /// Whether storage support is enabled. Uses centralized feature flag.
    /// </summary>
    public static bool IsEnabled => CosmosFeatures.StorageEnabled;

    private static void ThrowIfDisabled()
    {
        if (!IsEnabled)
        {
            throw new InvalidOperationException("Storage support is disabled. Set CosmosEnableStorage=true in your csproj to enable it.");
        }
    }

    private static IBlockDevice? s_primaryDevice;
    private static IBlockDevice?[]? s_devices;
    private static int s_deviceCount;
    private static List<Partition>? s_partitions;
    private static bool s_initialized;

    /// <summary>
    /// Gets whether the storage manager is initialized.
    /// </summary>
    public static bool IsInitialized => s_initialized;

    /// <summary>
    /// Gets the primary block device (first one registered).
    /// </summary>
    public static IBlockDevice? PrimaryDevice => s_primaryDevice;

    /// <summary>
    /// Gets the number of registered block devices.
    /// </summary>
    public static int DeviceCount => s_deviceCount;

    /// <summary>
    /// Partitions discovered across every registered device. Each entry is
    /// itself an <see cref="IBlockDevice"/> rooted at the partition's
    /// starting LBA, so filesystem drivers consume them without knowing
    /// whether the host disk is GPT-, MBR-, or unpartitioned.
    /// </summary>
    public static IReadOnlyList<Partition> Partitions => (IReadOnlyList<Partition>?)s_partitions ?? Array.Empty<Partition>();

    /// <summary>
    /// Initializes the storage manager.
    /// </summary>
    public static void Initialize()
    {
        ThrowIfDisabled();

        if (s_initialized)
        {
            return;
        }

        s_devices = new IBlockDevice[MaxDevices];
        s_deviceCount = 0;
        s_partitions = new List<Partition>();
        s_initialized = true;
    }

    /// <summary>
    /// Registers every block device produced by the HAL storage drivers
    /// (AHCI ports, NVMe namespaces). Called once during boot after the HAL
    /// has initialized the controllers.
    /// </summary>
    public static void RegisterHalDevices()
    {
        if (!IsEnabled)
        {
            return;
        }

        IReadOnlyList<BlockDevice> ports = Ahci.Ports;
        for (int i = 0; i < ports.Count; i++)
        {
            RegisterDevice(ports[i]);
        }

        IReadOnlyList<NvmeNamespace> nvmeNamespaces = Nvme.Namespaces;
        for (int i = 0; i < nvmeNamespaces.Count; i++)
        {
            RegisterDevice(nvmeNamespaces[i]);
        }
    }

    /// <summary>
    /// Registers a block device with the manager and scans it for a GPT or
    /// MBR partition table. Discovered partitions are appended to
    /// <see cref="Partitions"/>.
    /// </summary>
    /// <param name="device">The block device to register.</param>
    private static SchedSpinLock s_mutationLock;

    public static void RegisterDevice(IBlockDevice device)
    {
        if (device == null || s_devices == null || s_deviceCount >= s_devices.Length)
        {
            return;
        }

        // Serializes s_devices/s_partitions mutation for post-boot callers
        // (device hotplug paths, tests); reads are still unsynchronized —
        // enumerating Partitions while another thread rescans remains the
        // caller's problem. Re-registering a known device is a no-op:
        // RegisterDevice is public and unguarded (unlike Initialize), so a
        // second RegisterHalDevices call would otherwise double-count the
        // device and duplicate every partition under identical names.
        s_mutationLock.Acquire();
        try
        {
            for (int i = 0; i < s_deviceCount; i++)
            {
                if (ReferenceEquals(s_devices[i], device))
                {
                    return;
                }
            }

            s_devices[s_deviceCount++] = device;

            // First device becomes primary
            if (s_primaryDevice == null)
            {
                s_primaryDevice = device;
            }

            ScanPartitions(device);
        }
        finally
        {
            s_mutationLock.Release();
        }
    }

    /// <summary>
    /// Re-scan a previously-registered device for a partition table.
    /// Existing partitions belonging to that host are dropped first, so
    /// callers that just wrote a new layout (tests, formatting tools) get
    /// a clean partition list.
    /// </summary>
    public static void RescanPartitions(IBlockDevice device)
    {
        if (s_partitions == null || device == null)
        {
            return;
        }

        s_mutationLock.Acquire();
        try
        {
            for (int i = s_partitions.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(s_partitions[i].Host, device))
                {
                    s_partitions.RemoveAt(i);
                }
            }

            ScanPartitions(device);
        }
        finally
        {
            s_mutationLock.Release();
        }
    }

    private static void ScanPartitions(IBlockDevice device)
    {
        if (s_partitions == null)
        {
            return;
        }

        try
        {
            if (Gpt.IsGpt(device))
            {
                Serial.WriteString("[StorageManager] GPT detected on ");
                Serial.WriteString(device.Name);
                Serial.WriteString("\n");
                List<Gpt.PartitionEntry> entries = Gpt.Parse(device);
                for (int i = 0; i < entries.Count; i++)
                {
                    Gpt.PartitionEntry e = entries[i];
                    s_partitions.Add(new Partition(device, e.StartSector, e.SectorCount, (uint)i));
                }
                return;
            }

            if (Mbr.IsMbr(device))
            {
                Serial.WriteString("[StorageManager] MBR detected on ");
                Serial.WriteString(device.Name);
                Serial.WriteString("\n");
                List<Mbr.PartitionEntry> entries = Mbr.Parse(device);
                uint slot = 0;
                for (int i = 0; i < entries.Count; i++)
                {
                    Mbr.PartitionEntry e = entries[i];
                    s_partitions.Add(new Partition(device, e.StartSector, e.SectorCount, slot));
                    slot++;
                }

                if (Mbr.TryGetExtendedPartition(device, out ulong extendedStart))
                {
                    Serial.WriteString("[StorageManager] Extended partition found, walking EBR chain\n");
                    List<Mbr.PartitionEntry> logicals = Ebr.Parse(device, extendedStart);
                    for (int i = 0; i < logicals.Count; i++)
                    {
                        Mbr.PartitionEntry e = logicals[i];
                        s_partitions.Add(new Partition(device, e.StartSector, e.SectorCount, slot));
                        slot++;
                    }
                }

                if (slot > 0)
                {
                    return;
                }
            }

            // No partition table produced an entry. A filesystem formatted
            // straight onto the disk (a "superfloppy": mkfs.vfat / Windows
            // format of a raw image) carries the MBR's 0xAA55 boot signature
            // in its BPB sector, so it lands here rather than in a table
            // branch above. Probe the boot sector as a FAT BPB and, when its
            // claimed geometry fits the device, surface the whole disk as the
            // single partition the Partitions contract promises for
            // unpartitioned hosts. A blank disk fails the probe and stays
            // partitionless; GPT disks never reach here (empty GPTs return
            // above, keeping their on-disk structures out of partition I/O).
            Span<byte> boot = new byte[(int)device.BlockSize];
            device.ReadBlock(FatBootSector.BootSectorLba, 1, boot);
            if (FatBootSector.TryParse(boot, out FatBootSector? volume)
                && volume!.BytesPerSector == device.BlockSize
                && volume.TotalSectorCount <= device.BlockCount)
            {
                Serial.WriteString("[StorageManager] Unpartitioned filesystem volume detected on ");
                Serial.WriteString(device.Name);
                Serial.WriteString("\n");
                _partitions.Add(new Partition(device, 0, device.BlockCount, 0u));
            }
        }
        catch (Exception)
        {
            // Best-effort scan: a flaky device shouldn't block storage init —
            // but say so, or a real device fault (NVMe timeout throw per the
            // IBlockDevice error contract) is indistinguishable from "no
            // partition table". String-only output: this can run in the
            // phase-3 window where int formatting is off-limits.
            Serial.WriteString("[StorageManager] Partition scan failed on ");
            Serial.WriteString(device.Name);
            Serial.WriteString("\n");
        }
    }

    /// <summary>
    /// Gets a block device by index.
    /// </summary>
    /// <param name="index">The device index.</param>
    /// <returns>The block device, or null if not found.</returns>
    public static IBlockDevice? GetDevice(int index)
    {
        ThrowIfDisabled();

        if (s_devices == null || index < 0 || index >= s_deviceCount)
        {
            return null;
        }

        return s_devices[index];
    }
}
