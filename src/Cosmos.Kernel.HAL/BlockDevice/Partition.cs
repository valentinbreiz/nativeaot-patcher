// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.HAL.BlockDevice.Enums;
using Cosmos.Kernel.System.IO;

namespace Cosmos.Kernel.HAL.BlockDevice;

/// <summary>
/// Partition class. Used to read and write blocks of data.
/// </summary>
public class Partition : BlockDevice
{
    /// <summary>
    /// Hosting device.
    /// </summary>
    public readonly BlockDevice Host;

    /// <summary>
    /// Starting sector.
    /// </summary>
    public readonly ulong StartingSector;

    public override BlockDeviceType Type => Host.Type;

    /// <summary>
    /// Create new instance of the <see cref="Partition"/> class.
    /// </summary>
    /// <param name="aHost">A hosting device.</param>
    /// <param name="startingSector">A starting sector.</param>
    /// <param name="sectorCount">A sector count.</param>
    public Partition(BlockDevice aHost, ulong startingSector, ulong sectorCount)
    {
        Host = aHost;
        StartingSector = startingSector;
        BlockCount = sectorCount;
        BlockSize = Host.BlockSize;
    }

    /// <summary>
    /// Read block from partition.
    /// </summary>
    /// <param name="aBlockNo">A block to read from.</param>
    /// <param name="aBlockCount">A number of blocks in the partition.</param>
    /// <param name="aData">A data that been read.</param>
    /// <exception cref="OverflowException">Thrown when data lenght is greater then Int32.MaxValue.</exception>
    /// <exception cref="Exception">Thrown when data size invalid.</exception>
    public override void ReadBlock(ulong aBlockNo, ulong aBlockCount, ref byte[] aData)
    {
        Serial.WriteString("ReadBlock(");
        Serial.WriteNumber(aBlockNo);
        Serial.WriteString(",'");
        Serial.WriteNumber(aBlockCount);
        Serial.WriteString(")\n");
        CheckDataSize(aData, aBlockCount);
        ulong xHostBlockNo = StartingSector + aBlockNo;
        CheckBlockNo(xHostBlockNo, aBlockCount);
        Host.ReadBlock(xHostBlockNo, aBlockCount, ref aData);
    }

    /// <summary>
    /// Write block to partition.
    /// </summary>
    /// <param name="aBlockNo">A block number to write to.</param>
    /// <param name="aBlockCount">A number of blocks in the partition.</param>
    /// <param name="aData">A data to write.</param>
    /// <exception cref="OverflowException">Thrown when data lenght is greater then Int32.MaxValue.</exception>
    /// <exception cref="Exception">Thrown when data size invalid.</exception>
    public override void WriteBlock(ulong aBlockNo, ulong aBlockCount, ref byte[] aData)
    {
        CheckDataSize(aData, aBlockCount);
        ulong xHostBlockNo = StartingSector + aBlockNo;
        CheckBlockNo(xHostBlockNo, aBlockCount);
        Host.WriteBlock(xHostBlockNo, aBlockCount, ref aData);
    }

    /// <summary>
    /// To string.
    /// </summary>
    /// <returns>string value.</returns>
    public override string ToString() => "Partition";
}
