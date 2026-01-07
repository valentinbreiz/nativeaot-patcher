// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.BlockDevice;

namespace Cosmos.Kernel.System.VFS;

/// <summary>
/// Storage manager for scanning and initializing partitions on block devices.
/// </summary>
public static class StorageManager
{
    /// <summary>
    /// Scan and initialize partitions on a block device.
    /// </summary>
    /// <param name="device">The block device to scan.</param>
    public static void ScanAndInitPartitions(BaseBlockDevice device)
    {
        if (GPT.IsGPTPartition(device))
        {
            GPT gpt = new(device);

            Serial.WriteString("[StorageManager] Found GPT with ");
            Serial.WriteNumber((uint)gpt.Partitions.Count);
            Serial.WriteString(" partitions\n");

            foreach (var part in gpt.Partitions)
            {
                var partition = new Partition(device, part.StartSector, part.SectorCount);
                Partition.Partitions.Add(partition);
            }
        }
        else if (MBR.IsMBR(device))
        {
            MBR mbr = new(device);

            Serial.WriteString("[StorageManager] Found MBR with ");
            Serial.WriteNumber((uint)mbr.Partitions.Count);
            Serial.WriteString(" partitions\n");

            if (mbr.EBRLocation != 0)
            {
                // EBR Detected
                Span<byte> ebrData = new byte[512];
                device.ReadBlock(mbr.EBRLocation, 1U, ebrData);
                EBR ebr = new(ebrData);

                foreach (var part in ebr.Partitions)
                {
                    var partition = new Partition(device, mbr.EBRLocation + part.StartSector, part.SectorCount);
                    Partition.Partitions.Add(partition);
                }
            }

            foreach (var part in mbr.Partitions)
            {
                var partition = new Partition(device, part.StartSector, part.SectorCount);
                Partition.Partitions.Add(partition);
            }
        }
        else
        {
            Serial.WriteString("[StorageManager] No partition table found on device\n");
        }
    }

    /// <summary>
    /// Scan all provided block devices for partitions.
    /// </summary>
    /// <param name="devices">The block devices to scan.</param>
    public static void ScanAllDevices(IEnumerable<BaseBlockDevice> devices)
    {
        foreach (var device in devices)
        {
            Serial.WriteString("[StorageManager] Scanning device...\n");
            ScanAndInitPartitions(device);
        }
    }
}
