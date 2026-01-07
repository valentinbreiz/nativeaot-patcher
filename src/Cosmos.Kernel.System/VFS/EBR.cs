// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.System.VFS;

/// <summary>
/// EBR (Extended Boot Record) parser for logical partitions in extended partitions.
/// </summary>
public class EBR
{
    public List<PartInfo> Partitions = new List<PartInfo>();

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

    public EBR(Span<byte> aEBR)
    {
        ParsePartition(aEBR, 446);
        ParsePartition(aEBR, 462);
    }

    protected void ParsePartition(Span<byte> aEBR, int aLoc)
    {
        byte xSystemID = aEBR[aLoc + 4];
        // SystemID = 0 means no partition
        //TODO: Extended Partition Table
        if (xSystemID == 0x5 || xSystemID == 0xF || xSystemID == 0x85)
        {
            //Another EBR Detected
        }
        else if (xSystemID != 0)
        {
            ulong xStartSector = BitConverter.ToUInt32(aEBR.Slice(aLoc + 8, 4));
            ulong xSectorCount = BitConverter.ToUInt32(aEBR.Slice(aLoc + 12, 4));

            var xPartInfo = new PartInfo(xSystemID, xStartSector, xSectorCount);
            Partitions.Add(xPartInfo);
        }
    }
}
