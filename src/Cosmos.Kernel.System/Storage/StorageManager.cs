// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.Devices.Storage;
using Cosmos.Kernel.HAL.Interfaces.Devices;

namespace Cosmos.Kernel.System.Storage;

/// <summary>
/// Manages block storage devices.
/// </summary>
public static class StorageManager
{
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

    private static IBlockDevice? _primaryDevice;
    private static IBlockDevice?[]? _devices;
    private static int _deviceCount;
    private static List<Partition>? _partitions;
    private static bool _initialized;

    /// <summary>
    /// Gets whether the storage manager is initialized.
    /// </summary>
    public static bool IsInitialized => _initialized;

    /// <summary>
    /// Gets the primary block device (first one registered).
    /// </summary>
    public static IBlockDevice? PrimaryDevice => _primaryDevice;

    /// <summary>
    /// Gets the number of registered block devices.
    /// </summary>
    public static int DeviceCount => _deviceCount;

    /// <summary>
    /// Partitions discovered across every registered device. Each entry is
    /// itself an <see cref="IBlockDevice"/> rooted at the partition's
    /// starting LBA, so filesystem drivers consume them without knowing
    /// whether the host disk is GPT-, MBR-, or unpartitioned.
    /// </summary>
    public static IReadOnlyList<Partition> Partitions => (IReadOnlyList<Partition>?)_partitions ?? Array.Empty<Partition>();

    /// <summary>
    /// Initializes the storage manager.
    /// </summary>
    public static void Initialize()
    {
        ThrowIfDisabled();

        if (_initialized)
        {
            return;
        }

        _devices = new IBlockDevice[8];
        _deviceCount = 0;
        _partitions = new List<Partition>();
        _initialized = true;
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

        List<BlockDevice> ports = Ahci.Ports;
        for (int i = 0; i < ports.Count; i++)
        {
            RegisterDevice(ports[i]);
        }

        List<NvmeNamespace> nvmeNamespaces = Nvme.Namespaces;
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
    public static void RegisterDevice(IBlockDevice device)
    {
        if (device == null || _devices == null || _deviceCount >= _devices.Length)
        {
            return;
        }

        _devices[_deviceCount++] = device;

        // First device becomes primary
        if (_primaryDevice == null)
        {
            _primaryDevice = device;
        }

        ScanPartitions(device);
    }

    /// <summary>
    /// Re-scan a previously-registered device for a partition table.
    /// Existing partitions belonging to that host are dropped first, so
    /// callers that just wrote a new layout (tests, formatting tools) get
    /// a clean partition list.
    /// </summary>
    public static void RescanPartitions(IBlockDevice device)
    {
        if (_partitions == null || device == null)
        {
            return;
        }

        for (int i = _partitions.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_partitions[i].Host, device))
            {
                _partitions.RemoveAt(i);
            }
        }

        ScanPartitions(device);
    }

    private static void ScanPartitions(IBlockDevice device)
    {
        if (_partitions == null)
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
                    _partitions.Add(new Partition(device, e.StartSector, e.SectorCount, $"{device.Name}p{i}"));
                }
                return;
            }

            if (Mbr.IsMbr(device))
            {
                Serial.WriteString("[StorageManager] MBR detected on ");
                Serial.WriteString(device.Name);
                Serial.WriteString("\n");
                List<Mbr.PartitionEntry> entries = Mbr.Parse(device);
                for (int i = 0; i < entries.Count; i++)
                {
                    Mbr.PartitionEntry e = entries[i];
                    _partitions.Add(new Partition(device, e.StartSector, e.SectorCount, $"{device.Name}p{i}"));
                }
            }
        }
        catch (Exception)
        {
            // Best-effort scan: a flaky device shouldn't block storage init.
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

        if (_devices == null || index < 0 || index >= _deviceCount)
        {
            return null;
        }

        return _devices[index];
    }
}
