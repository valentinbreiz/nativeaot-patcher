// This code is licensed under MIT license (see LICENSE for details)

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
        BitConverter.TryWriteBytes(protectiveMBR.Slice(454, 4), 1u);

        // Size in LBA (entire disk minus 1)
        uint sizeInLBA = (uint)(device.BlockCount > 0xFFFFFFFF ? 0xFFFFFFFF : device.BlockCount - 1);
        BitConverter.TryWriteBytes(protectiveMBR.Slice(458, 4), sizeInLBA);

        // MBR signature
        protectiveMBR[510] = 0x55;
        protectiveMBR[511] = 0xAA;

        device.WriteBlock(0, 1, protectiveMBR);

        // GPT Header at LBA 1
        Span<byte> gptHeader = new byte[512];
        gptHeader.Clear();

        // Signature "EFI PART"
        BitConverter.TryWriteBytes(gptHeader.Slice(0, 8), EFIPartitionSignature);

        // Revision (1.0)
        BitConverter.TryWriteBytes(gptHeader.Slice(8, 4), 0x00010000u);

        // Header size (92 bytes)
        BitConverter.TryWriteBytes(gptHeader.Slice(12, 4), 92u);

        // Header CRC32 (will be calculated later, set to 0 for now)
        BitConverter.TryWriteBytes(gptHeader.Slice(16, 4), 0u);

        // Reserved
        BitConverter.TryWriteBytes(gptHeader.Slice(20, 4), 0u);

        // Current LBA (1)
        BitConverter.TryWriteBytes(gptHeader.Slice(24, 8), 1UL);

        // Backup LBA (last sector)
        BitConverter.TryWriteBytes(gptHeader.Slice(32, 8), device.BlockCount - 1);

        // First usable LBA (after partition entries, typically LBA 34)
        BitConverter.TryWriteBytes(gptHeader.Slice(40, 8), 34UL);

        // Last usable LBA (before backup partition entries)
        BitConverter.TryWriteBytes(gptHeader.Slice(48, 8), device.BlockCount - 34);

        // Disk GUID (generate a simple one based on device hash)
        uint hash = (uint)device.GetHashCode();
        Span<byte> diskGuid = gptHeader.Slice(56, 16);
        BitConverter.TryWriteBytes(diskGuid.Slice(0, 4), hash);
        BitConverter.TryWriteBytes(diskGuid.Slice(4, 4), hash ^ 0x12345678);
        BitConverter.TryWriteBytes(diskGuid.Slice(8, 4), hash ^ 0x87654321);
        BitConverter.TryWriteBytes(diskGuid.Slice(12, 4), hash ^ 0xDEADBEEF);

        // Partition entries starting LBA (2)
        BitConverter.TryWriteBytes(gptHeader.Slice(72, 8), 2UL);

        // Number of partition entries (128)
        BitConverter.TryWriteBytes(gptHeader.Slice(80, 4), 128u);

        // Size of partition entry (128 bytes)
        BitConverter.TryWriteBytes(gptHeader.Slice(84, 4), 128u);

        // Partition entries CRC32 (0 for empty entries)
        BitConverter.TryWriteBytes(gptHeader.Slice(88, 4), 0u);

        device.WriteBlock(1, 1, gptHeader);

        // Clear partition entries (LBA 2-33)
        Span<byte> emptyBlock = new byte[512];
        emptyBlock.Clear();
        for (ulong i = 2; i < 34; i++)
        {
            device.WriteBlock(i, 1, emptyBlock);
        }
    }
}
