// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.HAL.Interfaces.Devices;

namespace Cosmos.Kernel.HAL.Devices.Storage;

/// <summary>
/// Abstract base class for all block storage devices.
/// </summary>
public abstract class BlockDevice : Device, IBlockDevice
{
    /// <inheritdoc />
    public ulong BlockCount { get; protected set; }

    /// <inheritdoc />
    public ulong BlockSize { get; protected set; }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <summary>
    /// Allocate a managed buffer sized for <paramref name="blockCount"/> blocks.
    /// </summary>
    public Span<byte> NewBlockArray(ulong blockCount)
    {
        return new byte[blockCount * BlockSize];
    }

    /// <inheritdoc />
    public abstract void ReadBlock(ulong blockNo, ulong blockCount, Span<byte> data);

    /// <inheritdoc />
    public abstract void WriteBlock(ulong blockNo, ulong blockCount, Span<byte> data);
}
