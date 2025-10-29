// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.HAL.BlockDevice;

public class BlockDeviceStream : Stream
{
    private readonly BaseBlockDevice _blockDevice;
    private long _position;
    public BlockDeviceStream(BaseBlockDevice blockDevice)
    {
        _blockDevice = blockDevice ?? throw new ArgumentNullException(nameof(blockDevice));
        CanRead = true;
        CanSeek = true;
        CanWrite = true;
        Length = (long)(blockDevice.BlockCount * blockDevice.BlockSize);
    }

    private static void CheckBuffer(int bufferSize, int count)
    {
        if (bufferSize < count)
        {
            throw new Exception("Buffer size must be more than or equal to count");
        }
    }

    public override void Flush() => throw new NotImplementedException();

    public override int Read(byte[] buffer, int offset, int count)
    {
        CheckBuffer(buffer.Length, count);
        ulong blockSize = _blockDevice.BlockSize;
        ulong startBlock = (ulong)(_position / (long)blockSize);
        int startOffsetInBlock = (int)(_position % (long)blockSize);

        // Last byte offset in the stream
        long endPos = _position + count;
        ulong endBlock = (ulong)((endPos - 1) / (long)blockSize);
        ulong blocksToRead = endBlock - startBlock + 1;

        Span<byte> fullData = _blockDevice.NewBlockArray(blocksToRead);
        _blockDevice.ReadBlock(startBlock, blocksToRead, fullData);

        int available = (int)Math.Min(count, (long)Length - _position);
        fullData.Slice(startOffsetInBlock, available).CopyTo(buffer.AsSpan(offset));

        _position += available;
        return available;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        CheckBuffer(buffer.Length - offset, count);

        ulong blockSize = _blockDevice.BlockSize;
        ulong startByte = (ulong)Position;
        ulong endByte = (ulong)(Position + count);

        ulong startBlock = startByte / blockSize;
        ulong endBlock = (endByte - 1) / blockSize;
        ulong blocksToWrite = endBlock - startBlock + 1;

        // Read existing blocks to preserve unchanged bytes (read–modify–write)
        Span<byte> temp = _blockDevice.NewBlockArray(blocksToWrite);
        _blockDevice.ReadBlock(startBlock, blocksToWrite, temp);

        int intraBlockOffset = (int)(startByte % blockSize);
        buffer.AsSpan(offset, count).CopyTo(temp.Slice(intraBlockOffset));

        _blockDevice.WriteBlock(startBlock, blocksToWrite, temp);

        Position += count;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        return origin switch
        {
            SeekOrigin.Begin => Position = offset,
            SeekOrigin.Current => Position += offset,
            SeekOrigin.End => Position = Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
    }

    public override void SetLength(long value) =>
        throw new NotSupportedException("Cannot resize block device streams.");

    public override bool CanRead { get; }
    public override bool CanSeek { get; }
    public override bool CanWrite { get; }
    public override long Length { get; }
    public override long Position { get; set; }
}
