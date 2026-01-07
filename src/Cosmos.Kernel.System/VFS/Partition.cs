// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.HAL.BlockDevice;

namespace Cosmos.Kernel.System.VFS;

/// <summary>
/// Partition class. Represents a partition on a block device.
/// Used to read and write blocks of data within a partition.
/// </summary>
public class Partition : BaseBlockDevice
{
    /// <summary>
    /// Hosting device.
    /// </summary>
    public readonly BaseBlockDevice Host;

    /// <summary>
    /// Starting sector of the partition.
    /// </summary>
    public readonly ulong StartingSector;

    /// <summary>
    /// Partition name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// List of all partitions.
    /// </summary>
    public static List<Partition> Partitions = new();

    /// <summary>
    /// Create new instance of the <see cref="Partition"/> class.
    /// </summary>
    /// <param name="aHost">A hosting device.</param>
    /// <param name="startingSector">A starting sector.</param>
    /// <param name="sectorCount">A sector count.</param>
    public Partition(BaseBlockDevice aHost, ulong startingSector, ulong sectorCount)
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
    /// <param name="aBlockCount">A number of blocks to read.</param>
    /// <param name="aData">A buffer to store read data.</param>
    /// <exception cref="OverflowException">Thrown when data length is greater than Int32.MaxValue.</exception>
    /// <exception cref="Exception">Thrown when data size invalid.</exception>
    public override void ReadBlock(ulong aBlockNo, ulong aBlockCount, Span<byte> aData)
    {
        CheckDataSize(aData, aBlockCount);
        ulong xHostBlockNo = StartingSector + aBlockNo;
        CheckBlockNo(xHostBlockNo, aBlockCount);
        Host.ReadBlock(xHostBlockNo, aBlockCount, aData);
    }

    /// <summary>
    /// Write block to partition.
    /// </summary>
    /// <param name="aBlockNo">A block number to write to.</param>
    /// <param name="aBlockCount">A number of blocks to write.</param>
    /// <param name="aData">A data to write.</param>
    /// <exception cref="OverflowException">Thrown when data length is greater than Int32.MaxValue.</exception>
    /// <exception cref="Exception">Thrown when data size invalid.</exception>
    public override void WriteBlock(ulong aBlockNo, ulong aBlockCount, Span<byte> aData)
    {
        CheckDataSize(aData, aBlockCount);
        ulong xHostBlockNo = StartingSector + aBlockNo;
        CheckBlockNo(xHostBlockNo, aBlockCount);
        Host.WriteBlock(xHostBlockNo, aBlockCount, aData);
    }

    /// <summary>
    /// Check data size.
    /// </summary>
    /// <param name="aData">A data to check the size of.</param>
    /// <param name="aBlockCount">Number of blocks used to store the data.</param>
    /// <exception cref="Exception">Thrown when data size invalid.</exception>
    protected void CheckDataSize(Span<byte> aData, ulong aBlockCount)
    {
        if ((ulong)aData.Length / BlockSize != aBlockCount)
        {
            throw new Exception("Invalid data size.");
        }
    }

    /// <summary>
    /// Check block number.
    /// </summary>
    /// <param name="aBlockNo">A block number to be checked.</param>
    /// <param name="aBlockCount">A block count.</param>
    protected void CheckBlockNo(ulong aBlockNo, ulong aBlockCount)
    {
        if (aBlockNo + aBlockCount >= Host.BlockCount)
        {
            //throw new Exception("Invalid block number.");
        }
    }

    /// <summary>
    /// To string.
    /// </summary>
    /// <returns>string value.</returns>
    public override string ToString()
    {
        return "Partition";
    }
}
