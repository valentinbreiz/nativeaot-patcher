// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.HAL.BlockDevice.Enums;

namespace Cosmos.Kernel.HAL.BlockDevice;

// This class should not support selecting a device or sub device.
// Each instance must control exactly one device. For example with ATA
// master/slave, each one needs its own device instance. For ATA
// this complicates things a bit because they share IO ports, but this
// is an intentional decision.
/// <summary>
/// BlockDevice abstract class.
/// </summary>
public abstract class BlockDevice
{
    /// <summary>
    /// Create new block array.
    /// </summary>
    /// <param name="aBlockCount">Number of blocks to alloc.</param>
    /// <returns>byte array.</returns>
    public byte[] NewBlockArray(ulong aBlockCount) => new byte[aBlockCount * BlockSize];

    /// <summary>
    /// Get block count.
    /// </summary>
    public ulong BlockCount { get; protected set; }

    /// <summary>
    /// block size.
    /// </summary>
    public ulong BlockSize { get; protected set; }

    public abstract BlockDeviceType Type { get; }

    // Only allow reading and writing whole blocks because many of the hardware
    // command work that way and we dont want to add complexity at the BlockDevice level.
    // public abstract void ReadBlock(UInt64 aBlockNo, UInt32 aBlockCount, byte[] aData);
    /// <summary>
    /// Read block from partition.
    /// </summary>
    /// <param name="aBlockNo">A block to read from.</param>
    /// <param name="aBlockCount">A number of blocks in the partition.</param>
    /// <param name="aData">A data that been read.</param>
    /// <exception cref="OverflowException">Thrown when data lenght is greater then Int32.MaxValue.</exception>
    /// <exception cref="Exception">Thrown when data size invalid.</exception>
    public abstract void ReadBlock(ulong aBlockNo, ulong aBlockCount, ref byte[] aData);

    /// <summary>
    /// Write block to partition.
    /// </summary>
    /// <param name="aBlockNo">A block number to write to.</param>
    /// <param name="aBlockCount">A number of blocks in the partition.</param>
    /// <param name="aData">A data to write.</param>
    /// <exception cref="OverflowException">Thrown when data lenght is greater then Int32.MaxValue.</exception>
    /// <exception cref="Exception">Thrown when data size invalid.</exception>
    public abstract void WriteBlock(ulong aBlockNo, ulong aBlockCount, ref byte[] aData);

    /// <summary>
    /// Check data size.
    /// </summary>
    /// <param name="aData">A data to check the size of.</param>
    /// <param name="aBlockCount">Number of blocks used to store the data.</param>
    /// <exception cref="OverflowException">Thrown when data lenght is greater then Int32.MaxValue.</exception>
    /// <exception cref="Exception">Thrown when data size invalid.</exception>
    protected void CheckDataSize(byte[] aData, ulong aBlockCount)
    {
        if ((ulong)aData.Length / BlockSize != aBlockCount)
        {
            throw new Exception("Invalid data size.");
        }
    }

    /// <summary>
    /// Check block number.
    /// Not implemented.
    /// </summary>
    /// <param name="aBlockNo">A block number to be checked.</param>
    /// <param name="aBlockCount">A block count.</param>
    protected void CheckBlockNo(ulong aBlockNo, ulong aBlockCount)
    {
        if (aBlockNo + aBlockCount >= BlockCount)
        {
            //throw new Exception("Invalid block number.");
        }
    }
}
