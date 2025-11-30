// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.HAL.BlockDevice;

namespace Cosmos.Kernel.Services.VFS;

public class PartitionStream : Stream
{
    private readonly Partition _partition;
    private readonly BlockDeviceStream _blockStream;

    public PartitionStream(Partition partition)
    {
        _partition = partition ?? throw new ArgumentNullException(nameof(partition));
        Length = (long)_partition.Size;
        _blockStream = new BlockDeviceStream(partition.BlockDevice);
    }

    public override void Flush() => _blockStream.Flush();

    public override int Read(byte[] buffer, int offset, int count)
    {
        return _blockStream.Read(buffer, offset + (int)_partition.Offset, count);
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

    public override void SetLength(long value) => throw new NotSupportedException("Cannot resize Partition streams.");

    public override void Write(byte[] buffer, int offset, int count)
    {
        _blockStream.Write(buffer, offset + (int)_partition.Offset, count);
    }

    public override bool CanRead => _blockStream.CanRead;
    public override bool CanSeek => _blockStream.CanSeek;
    public override bool CanWrite => _blockStream.CanWrite;
    public override long Length { get; }
    public override long Position { get; set; }
}
