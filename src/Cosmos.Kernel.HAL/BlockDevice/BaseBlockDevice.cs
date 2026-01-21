// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.HAL.BlockDevice;

public abstract class BaseBlockDevice
{

    public ulong BlockCount { get; protected set; }
    public ulong BlockSize { get; protected set; }

    public Span<byte> NewBlockArray(ulong aBlockCount)
    {
        return new byte[aBlockCount * BlockSize];
    }


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
    public abstract void ReadBlock(ulong aBlockNo, ulong aBlockCount, Span<byte> aData);

    /// <summary>
    /// Write block to partition.
    /// </summary>
    /// <param name="aBlockNo">A block number to write to.</param>
    /// <param name="aBlockCount">A number of blocks in the partition.</param>
    /// <param name="aData">A data to write.</param>
    /// <exception cref="OverflowException">Thrown when data lenght is greater then Int32.MaxValue.</exception>
    /// <exception cref="Exception">Thrown when data size invalid.</exception>
    public abstract void WriteBlock(ulong aBlockNo, ulong aBlockCount, Span<byte> aData);

}
