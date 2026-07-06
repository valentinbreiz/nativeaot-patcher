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

    /// <inheritdoc />
    public abstract void ReadBlock(ulong blockNo, ulong blockCount, Span<byte> data);

    /// <inheritdoc />
    public abstract void WriteBlock(ulong blockNo, ulong blockCount, ReadOnlySpan<byte> data);

    /// <inheritdoc />
    /// <remarks>Default is a no-op for devices without a volatile write cache.</remarks>
    public virtual void Flush()
    {
    }
}
