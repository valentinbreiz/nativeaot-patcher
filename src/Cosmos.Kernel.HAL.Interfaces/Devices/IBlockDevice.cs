// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.HAL.Interfaces.Devices;

/// <summary>
/// Interface for block storage devices (SATA, NVMe, virtio-blk, etc.).
/// Block-level read/write only — partitioning and filesystems sit above this.
/// </summary>
public interface IBlockDevice
{
    /// <summary>
    /// Total number of addressable blocks on the device.
    /// </summary>
    ulong BlockCount { get; }

    /// <summary>
    /// Block size in bytes (typically 512 or 4096).
    /// </summary>
    ulong BlockSize { get; }

    /// <summary>
    /// Human-readable device name (e.g. "SATA").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Read whole blocks starting at <paramref name="blockNo"/> into <paramref name="data"/>.
    /// </summary>
    /// <param name="blockNo">Logical block address to start reading from.</param>
    /// <param name="blockCount">Number of blocks to read.</param>
    /// <param name="data">Destination buffer; must be at least <paramref name="blockCount"/> * <see cref="BlockSize"/> bytes.</param>
    void ReadBlock(ulong blockNo, ulong blockCount, Span<byte> data);

    /// <summary>
    /// Write whole blocks starting at <paramref name="blockNo"/> from <paramref name="data"/>.
    /// </summary>
    /// <param name="blockNo">Logical block address to start writing to.</param>
    /// <param name="blockCount">Number of blocks to write.</param>
    /// <param name="data">Source buffer; must be at least <paramref name="blockCount"/> * <see cref="BlockSize"/> bytes.</param>
    void WriteBlock(ulong blockNo, ulong blockCount, Span<byte> data);
}
