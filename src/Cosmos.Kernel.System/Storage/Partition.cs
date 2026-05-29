// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.HAL.Devices.Storage;
using Cosmos.Kernel.HAL.Interfaces.Devices;

namespace Cosmos.Kernel.System.Storage;

/// <summary>
/// Block-device view of a single partition on a host disk. Read/Write are
/// translated to the host LBA space by adding <see cref="StartingSector"/>.
/// </summary>
public sealed class Partition : BlockDevice
{
    private readonly IBlockDevice _host;
    private readonly string _name;

    /// <summary>The disk this partition lives on.</summary>
    public IBlockDevice Host => _host;

    /// <summary>Absolute LBA on the host where the partition begins.</summary>
    public ulong StartingSector { get; }

    public override string Name => _name;

    public Partition(IBlockDevice host, ulong startingSector, ulong sectorCount, string name)
    {
        _host = host;
        _name = name;
        StartingSector = startingSector;
        BlockCount = sectorCount;
        BlockSize = host.BlockSize;
    }

    public override void ReadBlock(ulong blockNo, ulong blockCount, Span<byte> data)
    {
        CheckBounds(blockNo, blockCount);
        _host.ReadBlock(StartingSector + blockNo, blockCount, data);
    }

    public override void WriteBlock(ulong blockNo, ulong blockCount, Span<byte> data)
    {
        CheckBounds(blockNo, blockCount);
        _host.WriteBlock(StartingSector + blockNo, blockCount, data);
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
