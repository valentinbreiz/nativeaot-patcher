// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.HAL.BlockDevice;

namespace Cosmos.Kernel.System.VFS;

/// <summary>
/// A stream wrapper for reading/writing to a partition.
/// Since Partition extends BaseBlockDevice, this is a convenience wrapper.
/// </summary>
public class PartitionStream : Stream
{
    private readonly Partition _partition;
    private readonly BlockDeviceStream _blockStream;

    public PartitionStream(Partition partition)
    {
        _partition = partition ?? throw new ArgumentNullException(nameof(partition));
        // Partition is now a BlockDevice, so pass it directly
        _blockStream = new BlockDeviceStream(partition);
        Length = (long)(partition.BlockCount * partition.BlockSize);
    }

    public override void Flush() => _blockStream.Flush();

    public override int Read(byte[] buffer, int offset, int count)
    {
        return _blockStream.Read(buffer, offset, count);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        return _blockStream.Seek(offset, origin);
    }

    public override void SetLength(long value) => throw new NotSupportedException("Cannot resize Partition streams.");

    public override void Write(byte[] buffer, int offset, int count)
    {
        _blockStream.Write(buffer, offset, count);
    }

    public override bool CanRead => _blockStream.CanRead;
    public override bool CanSeek => _blockStream.CanSeek;
    public override bool CanWrite => _blockStream.CanWrite;
    public override long Length { get; }

    public override long Position
    {
        get => _blockStream.Position;
        set => _blockStream.Position = value;
    }
}
