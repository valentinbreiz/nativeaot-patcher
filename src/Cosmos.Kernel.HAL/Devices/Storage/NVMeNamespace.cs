// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core.IO;

namespace Cosmos.Kernel.HAL.Devices.Storage;

/// <summary>
/// One NVMe namespace exposed as an <see cref="HAL.Interfaces.Devices.IBlockDevice"/>.
/// Reads and writes are issued one LBA at a time through the parent
/// controller's per-slot bounce buffers — multiple namespaces (or
/// concurrent callers on the same namespace) execute in parallel up to
/// the controller's I/O queue depth.
/// </summary>
public unsafe class NVMeNamespace : BlockDevice
{
    private readonly NVMeController _controller;
    private readonly uint _nsid;

    public override string Name => "NVMe";

    public NVMeNamespace(NVMeController controller, uint nsid, ulong blockCount, ulong blockSize)
    {
        _controller = controller;
        _nsid = nsid;
        BlockCount = blockCount;
        BlockSize = blockSize;
    }

    public override void ReadBlock(ulong blockNo, ulong blockCount, Span<byte> data)
    {
        int sector = (int)BlockSize;
        for (ulong i = 0; i < blockCount; i++)
        {
            Span<byte> dst = data.Slice((int)i * sector, sector);
            uint sc = _controller.Read(_nsid, blockNo + i, dst, numLogicalBlocksMinusOne: 0);
            if (sc != 0)
            {
                Serial.WriteString("[NVMe] Read failed lba=");
                Serial.WriteNumber(blockNo + i);
                Serial.WriteString(" status=0x");
                Serial.WriteHex(sc);
                Serial.WriteString("\n");
                throw new Exception("NVMe Read error");
            }
        }
    }

    public override void WriteBlock(ulong blockNo, ulong blockCount, Span<byte> data)
    {
        int sector = (int)BlockSize;
        for (ulong i = 0; i < blockCount; i++)
        {
            ReadOnlySpan<byte> src = data.Slice((int)i * sector, sector);
            uint sc = _controller.Write(_nsid, blockNo + i, src, numLogicalBlocksMinusOne: 0);
            if (sc != 0)
            {
                Serial.WriteString("[NVMe] Write failed lba=");
                Serial.WriteNumber(blockNo + i);
                Serial.WriteString(" status=0x");
                Serial.WriteHex(sc);
                Serial.WriteString("\n");
                throw new Exception("NVMe Write error");
            }
        }
    }
}
