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
        int byteOffset = (int)(blockNo * BlockSize);
        int byteLen = (int)(blockCount * BlockSize);
        _storage.AsSpan(byteOffset, byteLen).CopyTo(data);
    }

    public void WriteBlock(ulong blockNo, ulong blockCount, Span<byte> data)
    {
        int byteOffset = (int)(blockNo * BlockSize);
        int byteLen = (int)(blockCount * BlockSize);
        data.Slice(0, byteLen).CopyTo(_storage.AsSpan(byteOffset, byteLen));
    }
}
