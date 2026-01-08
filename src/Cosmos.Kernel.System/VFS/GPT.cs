// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.BlockDevice;

namespace Cosmos.Kernel.System.VFS;

/// <summary>
/// GPT (GUID Partition Table) parser.
/// </summary>
public class GPT
{
    // Signature: "EFI PART"
    private const ulong EFIPartitionSignature = 0x5452415020494645;

    public List<GPartInfo> Partitions = new List<GPartInfo>();

    public class GPartInfo
    {
        public readonly Guid PartitionType;
        public readonly Guid PartitionGuid;
        public readonly ulong StartSector;
        public readonly ulong SectorCount;

        public GPartInfo(Guid aPartitionType, Guid aPartitionGuid, ulong aStartSector, ulong aSectorCount)
        {
            PartitionType = aPartitionType;
            PartitionGuid = aPartitionGuid;
            StartSector = aStartSector;
            SectorCount = aSectorCount;
        }
    }

    public GPT(BaseBlockDevice aBlockDevice)
    {
        Span<byte> gptHeader = new byte[512];
        aBlockDevice.ReadBlock(1, 1, gptHeader);

        // Start of partition entries
        ulong partEntryStart = BitConverter.ToUInt64(gptHeader.Slice(72, 8));
        ulong numPartitions = BitConverter.ToUInt32(gptHeader.Slice(80, 4));
        ulong partSize = BitConverter.ToUInt32(gptHeader.Slice(84, 4));

        ulong partitionsPerSector = 512 / partSize;

        for (ulong i = 0; i < numPartitions / partitionsPerSector; i++)
        {
            Span<byte> partData = new byte[512];
            aBlockDevice.ReadBlock(partEntryStart + i, 1, partData);

            for (uint j = 0; j < partitionsPerSector; j++)
            {
                ParsePartition(partData, (int)(j * partSize));
            }
        }
    }

    private void ParsePartition(Span<byte> partData, int off)
    {
        byte[] guidArray = new byte[16];

        partData.Slice(off, 16).CopyTo(guidArray);
        var partType = new Guid(guidArray);

        partData.Slice(off + 16, 16).CopyTo(guidArray);
        var partGuid = new Guid(guidArray);

        ulong startLBA = BitConverter.ToUInt64(partData.Slice(off + 32, 8));
        ulong endLBA = BitConverter.ToUInt64(partData.Slice(off + 40, 8));

        // endLBA + 1 because endLBA is inclusive
        ulong count = endLBA + 1 - startLBA;

        if (partType != Guid.Empty && partGuid != Guid.Empty)
        {
            Partitions.Add(new GPartInfo(partType, partGuid, startLBA, count));
        }
    }

    /// <summary>
    /// Check if a block device has a GPT partition table.
    /// </summary>
    /// <param name="aBlockDevice">The block device to check.</param>
    /// <returns>True if the device has a GPT signature.</returns>
    public static bool IsGPTPartition(BaseBlockDevice aBlockDevice)
    {
        Span<byte> gptHeader = new byte[512];
        aBlockDevice.ReadBlock(1, 1, gptHeader);

        ulong signature = BitConverter.ToUInt64(gptHeader.Slice(0, 8));

        return signature == EFIPartitionSignature;
    }

    /// <summary>
    /// Creates a GPT partition table on a disk.
    /// </summary>
    /// <param name="device">The block device to write the GPT to.</param>
    public static void CreateGPT(BaseBlockDevice device)
    {
        // First, write a protective MBR at LBA 0
        Span<byte> protectiveMBR = new byte[512];
        protectiveMBR.Clear();

        // Protective MBR partition entry at offset 446
        protectiveMBR[446] = 0x00;      // Boot indicator
        protectiveMBR[447] = 0x00;      // Starting head
        protectiveMBR[448] = 0x02;      // Starting sector (bits 0-5), cylinder high (bits 6-7)
        protectiveMBR[449] = 0x00;      // Starting cylinder low
        protectiveMBR[450] = 0xEE;      // System ID (0xEE = GPT protective)
        protectiveMBR[451] = 0xFF;      // Ending head
        protectiveMBR[452] = 0xFF;      // Ending sector/cylinder
        protectiveMBR[453] = 0xFF;      // Ending cylinder

        // Starting LBA (1)
        WriteUInt32(protectiveMBR, 454, 1u);

        // Size in LBA (entire disk minus 1)
        uint sizeInLBA = (uint)(device.BlockCount > 0xFFFFFFFF ? 0xFFFFFFFF : device.BlockCount - 1);
        WriteUInt32(protectiveMBR, 458, sizeInLBA);

        // MBR signature
        protectiveMBR[510] = 0x55;
        protectiveMBR[511] = 0xAA;

        Serial.WriteString("[GPT] Writing protective MBR at LBA 0...\n");
        device.WriteBlock(0, 1, protectiveMBR);
        Serial.WriteString("[GPT] Protective MBR written\n");

        // GPT Header at LBA 1
        Serial.WriteString("[GPT] Allocating GPT header buffer...\n");
        Span<byte> gptHeader = new byte[512];
        Serial.WriteString("[GPT] Clearing buffer...\n");
        gptHeader.Clear();
        Serial.WriteString("[GPT] Buffer ready\n");

        // Signature "EFI PART"
        Serial.WriteString("[GPT] Writing EFI signature...\n");
        WriteUInt64(gptHeader, 0, EFIPartitionSignature);
        Serial.WriteString("[GPT] EFI signature written\n");

        // Revision (1.0)
        Serial.WriteString("[GPT] Writing revision...\n");
        gptHeader[8] = 0x00;
        gptHeader[9] = 0x00;
        gptHeader[10] = 0x01;
        gptHeader[11] = 0x00;
        Serial.WriteString("[GPT] Revision written\n");

        // Header size (92 bytes)
        Serial.WriteString("[GPT] Writing header size...\n");
        gptHeader[12] = 92;
        gptHeader[13] = 0;
        gptHeader[14] = 0;
        gptHeader[15] = 0;
        Serial.WriteString("[GPT] Header size written\n");

        // Header CRC32 (will be calculated later, set to 0 for now)
        Serial.WriteString("[GPT] Writing remaining header fields...\n");
        WriteUInt32(gptHeader, 16, 0u);

        // Reserved
        WriteUInt32(gptHeader, 20, 0u);

        // Current LBA (1)
        WriteUInt64(gptHeader, 24, 1UL);

        // Backup LBA (last sector)
        WriteUInt64(gptHeader, 32, device.BlockCount - 1);

        // First usable LBA (after partition entries, typically LBA 34)
        WriteUInt64(gptHeader, 40, 34UL);

        // Last usable LBA (before backup partition entries)
        WriteUInt64(gptHeader, 48, device.BlockCount - 34);

        // Disk GUID (generate from device hash)
        Serial.WriteString("[GPT] Writing disk GUID...\n");
        uint guidPart = (uint)device.GetHashCode();
        WriteUInt32(gptHeader, 56, guidPart);
        WriteUInt32(gptHeader, 60, guidPart ^ 0x12345678);
        WriteUInt32(gptHeader, 64, guidPart ^ 0x87654321);
        WriteUInt32(gptHeader, 68, guidPart ^ 0xDEADBEEF);
        Serial.WriteString("[GPT] GUID written\n");

        // Partition entries starting LBA (2)
        WriteUInt64(gptHeader, 72, 2UL);

        // Number of partition entries (128)
        WriteUInt32(gptHeader, 80, 128u);

        // Size of partition entry (128 bytes)
        WriteUInt32(gptHeader, 84, 128u);

        // Partition entries CRC32 (0 for empty entries)
        WriteUInt32(gptHeader, 88, 0u);
        Serial.WriteString("[GPT] All header fields written\n");

        Serial.WriteString("[GPT] Writing GPT header at LBA 1...\n");
        device.WriteBlock(1, 1, gptHeader);
        Serial.WriteString("[GPT] GPT header written\n");

        // Clear partition entries (LBA 2-33)
        Serial.WriteString("[GPT] Clearing partition entries (LBA 2-33)...\n");
        Span<byte> emptyBlock = new byte[512];
        emptyBlock.Clear();
        for (ulong i = 2; i < 34; i++)
        {
            device.WriteBlock(i, 1, emptyBlock);
        }
        Serial.WriteString("[GPT] GPT creation complete\n");
    }

    /// <summary>
    /// Adds a partition to a GPT disk.
    /// </summary>
    /// <param name="device">The block device.</param>
    /// <param name="startSector">The starting LBA of the partition.</param>
    /// <param name="sectorCount">The number of sectors in the partition.</param>
    /// <returns>True if the partition was added successfully.</returns>
    public static bool AddPartition(BaseBlockDevice device, ulong startSector, ulong sectorCount)
    {
        Serial.WriteString("[GPT] AddPartition: start=");
        Serial.WriteNumber(startSector);
        Serial.WriteString(" count=");
        Serial.WriteNumber(sectorCount);
        Serial.WriteString("\n");

        // Read GPT header
        Span<byte> gptHeader = new byte[512];
        device.ReadBlock(1, 1, gptHeader);

        ulong signature = BitConverter.ToUInt64(gptHeader.Slice(0, 8));
        if (signature != EFIPartitionSignature)
        {
            Serial.WriteString("[GPT] ERROR: No GPT signature found\n");
            return false;
        }

        ulong partEntryStart = BitConverter.ToUInt64(gptHeader.Slice(72, 8));
        uint numPartitions = BitConverter.ToUInt32(gptHeader.Slice(80, 4));
        uint partSize = BitConverter.ToUInt32(gptHeader.Slice(84, 4));

        Serial.WriteString("[GPT] Partition entries at LBA ");
        Serial.WriteNumber(partEntryStart);
        Serial.WriteString(", max ");
        Serial.WriteNumber(numPartitions);
        Serial.WriteString(" partitions\n");

        // Find first empty partition entry
        uint entriesPerSector = 512 / partSize;
        int emptySlot = -1;

        for (ulong i = 0; i < numPartitions / entriesPerSector && emptySlot < 0; i++)
        {
            Span<byte> partData = new byte[512];
            device.ReadBlock(partEntryStart + i, 1, partData);

            for (uint j = 0; j < entriesPerSector; j++)
            {
                int offset = (int)(j * partSize);

                // Check if partition type GUID is zero (empty slot)
                bool isEmpty = true;
                for (int k = 0; k < 16; k++)
                {
                    if (partData[offset + k] != 0)
                    {
                        isEmpty = false;
                        break;
                    }
                }

                if (isEmpty)
                {
                    emptySlot = (int)(i * entriesPerSector + j);
                    Serial.WriteString("[GPT] Found empty slot at index ");
                    Serial.WriteNumber((ulong)emptySlot);
                    Serial.WriteString("\n");

                    // Write partition entry
                    // Partition Type GUID: Basic Data Partition (EBD0A0A2-B9E5-4433-87C0-68B6B72699C7)
                    partData[offset + 0] = 0xA2;
                    partData[offset + 1] = 0xA0;
                    partData[offset + 2] = 0xD0;
                    partData[offset + 3] = 0xEB;
                    partData[offset + 4] = 0xE5;
                    partData[offset + 5] = 0xB9;
                    partData[offset + 6] = 0x33;
                    partData[offset + 7] = 0x44;
                    partData[offset + 8] = 0x87;
                    partData[offset + 9] = 0xC0;
                    partData[offset + 10] = 0x68;
                    partData[offset + 11] = 0xB6;
                    partData[offset + 12] = 0xB7;
                    partData[offset + 13] = 0x26;
                    partData[offset + 14] = 0x99;
                    partData[offset + 15] = 0xC7;

                    // Unique Partition GUID (generate from start sector)
                    uint partGuid = (uint)(startSector ^ sectorCount ^ (ulong)emptySlot);
                    WriteUInt32(partData, offset + 16, partGuid);
                    WriteUInt32(partData, offset + 20, partGuid ^ 0x12345678);
                    WriteUInt32(partData, offset + 24, partGuid ^ 0x87654321);
                    WriteUInt32(partData, offset + 28, partGuid ^ 0xDEADBEEF);

                    // Start LBA
                    WriteUInt64(partData, offset + 32, startSector);

                    // End LBA (inclusive, so subtract 1)
                    WriteUInt64(partData, offset + 40, startSector + sectorCount - 1);

                    // Attributes (0 = none)
                    WriteUInt64(partData, offset + 48, 0);

                    // Partition name (skip for now, leave as zeros)

                    // Write back the partition entry sector
                    device.WriteBlock(partEntryStart + i, 1, partData);
                    Serial.WriteString("[GPT] Partition entry written\n");
                    break;
                }
            }
        }

        if (emptySlot < 0)
        {
            Serial.WriteString("[GPT] ERROR: No empty partition slots\n");
            return false;
        }

        return true;
    }

    private static void WriteUInt32(Span<byte> dest, int offset, uint value)
    {
        dest[offset] = (byte)(value & 0xFF);
        dest[offset + 1] = (byte)((value >> 8) & 0xFF);
        dest[offset + 2] = (byte)((value >> 16) & 0xFF);
        dest[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    private static void WriteUInt64(Span<byte> dest, int offset, ulong value)
    {
        dest[offset] = (byte)(value & 0xFF);
        dest[offset + 1] = (byte)((value >> 8) & 0xFF);
        dest[offset + 2] = (byte)((value >> 16) & 0xFF);
        dest[offset + 3] = (byte)((value >> 24) & 0xFF);
        dest[offset + 4] = (byte)((value >> 32) & 0xFF);
        dest[offset + 5] = (byte)((value >> 40) & 0xFF);
        dest[offset + 6] = (byte)((value >> 48) & 0xFF);
        dest[offset + 7] = (byte)((value >> 56) & 0xFF);
    }
}
