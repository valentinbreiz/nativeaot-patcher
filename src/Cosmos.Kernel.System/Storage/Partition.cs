// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.HAL.Devices.Storage;
using Cosmos.Kernel.HAL.Interfaces.Devices;

namespace Cosmos.Kernel.System.Storage;

/// <summary>
/// Block-device view of a single partition on a host disk. Read/Write are
/// translated to the host LBA space by adding <see cref="StartSector"/>.
/// </summary>
public sealed class Partition : BlockDevice
{
    private readonly IBlockDevice _host;
    private readonly string _name;

    /// <summary>The disk this partition lives on.</summary>
    public IBlockDevice Host => _host;

    /// <summary>Absolute LBA on the host where the partition begins.</summary>
    public ulong StartSector { get; }

    /// <inheritdoc />
    public override string Name => _name;

    /// <summary>Creates a block-device view of a partition on a host disk.</summary>
    /// <param name="host">The disk this partition lives on.</param>
    /// <param name="startSector">Absolute LBA on the host where the partition begins.</param>
    /// <param name="sectorCount">Length of the partition in sectors.</param>
    /// <param name="name">Display name for the partition.</param>
    public Partition(IBlockDevice host, ulong startSector, ulong sectorCount, string name)
    {
        _host = host;
        _name = name;
        StartSector = startSector;
        BlockCount = sectorCount;
        BlockSize = host.BlockSize;
    }

    /// <inheritdoc />
    public override void ReadBlock(ulong blockNo, ulong blockCount, Span<byte> data)
    {
        CheckBounds(blockNo, blockCount);
        _host.ReadBlock(StartSector + blockNo, blockCount, data);
    }

    /// <inheritdoc />
    public override void WriteBlock(ulong blockNo, ulong blockCount, ReadOnlySpan<byte> data)
    {
        CheckBounds(blockNo, blockCount);
        _host.WriteBlock(StartSector + blockNo, blockCount, data);
    }

    /// <inheritdoc />
    public override void Flush()
    {
        _host.Flush();
    }

    private void CheckBounds(ulong blockNo, ulong blockCount)
    {
        // Overflow-safe: `blockNo + blockCount` would wrap for a blockNo near
        // ulong.MaxValue and slip past a naive `sum > BlockCount` check.
        if (blockNo > BlockCount || blockCount > BlockCount - blockNo)
        {
            throw new ArgumentOutOfRangeException(nameof(blockNo), "Partition I/O extends beyond partition end.");
        }
    }
}
