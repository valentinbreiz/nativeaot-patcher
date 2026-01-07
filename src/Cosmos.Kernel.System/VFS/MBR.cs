// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.BlockDevice;

namespace Cosmos.Kernel.System.VFS;

/// <summary>
/// MBR (Master Boot Record) partition table parser.
/// It's not a BlockDevice, but its related to "fixed" devices
/// and necessary to create partition block devices.
/// </summary>
public class MBR
{
    // TODO Lock this so other code cannot add/remove/modify the list
    // Can make a locked list class which wraps a list<>
    public List<PartInfo> Partitions = new List<PartInfo>();

    public uint EBRLocation = 0;

    public class PartInfo
    {
        public readonly byte SystemID;
        public readonly ulong StartSector;
        public readonly ulong SectorCount;

        public PartInfo(byte aSystemID, ulong aStartSector, ulong aSectorCount)
        {
            SystemID = aSystemID;
            StartSector = aStartSector;
            SectorCount = aSectorCount;
        }
    }

    public MBR(BaseBlockDevice device)
    {
        Span<byte> aMBR = device.NewBlockArray(1);
        device.ReadBlock(0, 1, aMBR);

        ParsePartition(aMBR, 446);
        ParsePartition(aMBR, 462);
        ParsePartition(aMBR, 478);
        ParsePartition(aMBR, 494);
    }

    protected void ParsePartition(Span<byte> aMBR, int aLoc)
    {
        byte xSystemID = aMBR[aLoc + 4];
        // SystemID = 0 means no partition

        if (xSystemID == 0x5 || xSystemID == 0xF || xSystemID == 0x85)
        {
            //Extended Partition Detected
            //DOS only knows about 05, Windows 95 introduced 0F, Linux introduced 85
            //Search for logical volumes
            //http://thestarman.pcministry.com/asm/mbr/PartTables2.htm
            EBRLocation = BitConverter.ToUInt32(aMBR.Slice(aLoc + 8, 4));
        }
        else if (xSystemID != 0)
        {
            ulong xStartSector = BitConverter.ToUInt32(aMBR.Slice(aLoc + 8, 4));
            ulong xSectorCount = BitConverter.ToUInt32(aMBR.Slice(aLoc + 12, 4));

            var xPartInfo = new PartInfo(xSystemID, xStartSector, xSectorCount);
            Partitions.Add(xPartInfo);
        }
    }

    /// <summary>
    /// Creates a MBR partition table on a disk
    /// </summary>
    /// <param name="aDevice">The device to be written a partition table.</param>
    /// <exception cref="ArgumentNullException">
    /// <list type="bullet">
    /// <item>Thrown when aDevice is null.</item>
    /// </list>
    /// </exception>
    public void CreateMBR(BaseBlockDevice aDevice)
    {
        Serial.WriteString("[MBR] CreateMBR called\n");

        if (aDevice == null)
        {
            throw new ArgumentNullException(nameof(aDevice));
        }

        Serial.WriteString("[MBR] Allocating buffer...\n");
        Span<byte> mb = new byte[512];

        Serial.WriteString("[MBR] Writing boot code...\n");
        //Boot code
        BitConverter.TryWriteBytes(mb.Slice(0, 4), 0x1000B8FAu);
        BitConverter.TryWriteBytes(mb.Slice(4, 4), 0x00BCD08Eu);
        BitConverter.TryWriteBytes(mb.Slice(8, 4), 0x0000B8B0u);
        BitConverter.TryWriteBytes(mb.Slice(12, 4), 0xC08ED88Eu);
        BitConverter.TryWriteBytes(mb.Slice(16, 4), 0x7C00BEFBu);
        BitConverter.TryWriteBytes(mb.Slice(20, 4), 0xB90600BFu);
        BitConverter.TryWriteBytes(mb.Slice(24, 4), 0xA4F30200u);
        BitConverter.TryWriteBytes(mb.Slice(28, 4), 0x000621EAu);
        BitConverter.TryWriteBytes(mb.Slice(32, 4), 0x07BEBE07u);
        BitConverter.TryWriteBytes(mb.Slice(36, 4), 0x0B750438u);
        BitConverter.TryWriteBytes(mb.Slice(40, 4), 0x8110C683u);
        BitConverter.TryWriteBytes(mb.Slice(44, 4), 0x7507FEFEu);
        BitConverter.TryWriteBytes(mb.Slice(48, 4), 0xB416EBF3u);
        BitConverter.TryWriteBytes(mb.Slice(52, 4), 0xBB01B002u);
        BitConverter.TryWriteBytes(mb.Slice(56, 4), 0x80B27C00u);
        BitConverter.TryWriteBytes(mb.Slice(60, 4), 0x8B01748Au);
        BitConverter.TryWriteBytes(mb.Slice(64, 4), 0x13CD024Cu);
        BitConverter.TryWriteBytes(mb.Slice(68, 4), 0x007C00EAu);
        BitConverter.TryWriteBytes(mb.Slice(72, 4), 0x00FEEB00u);

        Serial.WriteString("[MBR] Writing disk ID...\n");
        //Unique disk ID, is used to separate different drives
        // Use a simple constant instead of GetHashCode which may not work in kernel
        BitConverter.TryWriteBytes(mb.Slice(440, 4), 0x12345678u);

        Serial.WriteString("[MBR] Writing signature...\n");
        //Signature
        BitConverter.TryWriteBytes(mb.Slice(510, 2), (ushort)0xAA55);

        Serial.WriteString("[MBR] Calling WriteBlock...\n");
        aDevice.WriteBlock(0, 1, mb);
        Serial.WriteString("[MBR] CreateMBR done\n");
    }

    /// <summary>
    /// Writes the selected partition's information on the MBR
    /// </summary>
    /// <param name="partition">The partition whose information will be written.</param>
    /// <param name="partitionNo">The partition number (0-3).</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <list type="bullet">
    /// <item>Thrown when the partition number is larger or smaller than allowed partition number count.</item>
    /// </list>
    /// </exception>
    public void WritePartitionInformation(Partition partition, byte partitionNo)
    {
        if (partitionNo > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(partitionNo));
        }

        Span<byte> mb = new byte[512];
        partition.Host.ReadBlock(0, 1, mb);

        int offset = 446 + (partitionNo * 16);

        //TO DO: Implement the CHS starting / ending sector addresses and partition type
        mb[offset + 4] = 0x0B; // FAT32 partition type

        BitConverter.TryWriteBytes(mb.Slice(offset + 8, 4), (uint)partition.StartingSector);
        BitConverter.TryWriteBytes(mb.Slice(offset + 12, 4), (uint)partition.BlockCount);

        partition.Host.WriteBlock(0, 1, mb);
        ParsePartition(mb, offset);
    }

    /// <summary>
    /// Check if a block device has a valid MBR signature.
    /// </summary>
    /// <param name="device">The block device to check.</param>
    /// <returns>True if the device has a valid MBR signature (0xAA55).</returns>
    public static bool IsMBR(BaseBlockDevice device)
    {
        Span<byte> mbr = device.NewBlockArray(1);
        device.ReadBlock(0, 1, mbr);

        ushort signature = BitConverter.ToUInt16(mbr.Slice(510, 2));
        return signature == 0xAA55;
    }
}
