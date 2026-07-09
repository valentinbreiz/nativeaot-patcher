using System;
using Cosmos.Kernel.HAL.Interfaces.Devices;

namespace Cosmos.Kernel.Tests.Fat;

/// <summary>
/// RAM-backed <see cref="IBlockDevice"/> for FAT tests. No partitioning,
/// no DMA, just a flat byte array sliced by LBA.
/// </summary>
internal sealed class MemoryBlockDevice : IBlockDevice
{
    private readonly byte[] _storage;

    public MemoryBlockDevice(string name, ulong blockSize, ulong blockCount)
    {
        Name = name;
        BlockSize = blockSize;
        BlockCount = blockCount;
        _storage = new byte[blockSize * blockCount];
    }

    public string Name { get; }
    public ulong BlockSize { get; }
    public ulong BlockCount { get; }

    public void ReadBlock(ulong blockNo, ulong blockCount, Span<byte> data)
    {
        ThrowIfOutOfRange(blockNo, blockCount);
        int byteOffset = (int)(blockNo * BlockSize);
        int byteLen = (int)(blockCount * BlockSize);
        _storage.AsSpan(byteOffset, byteLen).CopyTo(data);
    }

    public void WriteBlock(ulong blockNo, ulong blockCount, ReadOnlySpan<byte> data)
    {
        ThrowIfOutOfRange(blockNo, blockCount);
        int byteOffset = (int)(blockNo * BlockSize);
        int byteLen = (int)(blockCount * BlockSize);
        data.Slice(0, byteLen).CopyTo(_storage.AsSpan(byteOffset, byteLen));
    }

    // IBlockDevice is throw-on-failure: validate before the (int) casts
    // below truncate — a byte offset that is a multiple of 2^32 would
    // otherwise silently alias sector 0, hiding exactly the wild-sector
    // driver bugs this device exists to expose.
    private void ThrowIfOutOfRange(ulong blockNo, ulong blockCount)
    {
        if (blockNo > BlockCount || blockCount > BlockCount - blockNo)
        {
            throw new ArgumentOutOfRangeException(nameof(blockNo));
        }
    }

    /// <summary>Number of <see cref="Flush"/> calls — lets tests assert durability points.</summary>
    public int FlushCount { get; private set; }

    public void Flush()
    {
        // RAM-backed store: nothing volatile to make durable; count the
        // calls so tests can assert the contract's durability points.
        FlushCount++;
    }
}
