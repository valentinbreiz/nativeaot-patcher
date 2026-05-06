// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.Devices.Storage;

namespace Cosmos.Kernel.HAL.Devices.Storage;

/// <summary>
/// One NVMe namespace exposed as an <see cref="HAL.Interfaces.Devices.IBlockDevice"/>.
/// Reads and writes go through the parent controller's shared 4 KiB
/// bounce buffer one LBA at a time — same shape as the SATA driver.
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
            uint sc = _controller.SubmitIo(
                NVMeIoOp.Read,
                _nsid,
                _controller.DmaBufferPhys,
                blockNo + i,
                numLogicalBlocksMinusOne: 0);
            if (sc != 0)
            {
                Serial.WriteString("[NVMe] Read failed lba=");
                Serial.WriteNumber(blockNo + i);
                Serial.WriteString(" status=0x");
                Serial.WriteHex(sc);
                Serial.WriteString("\n");
                throw new Exception("NVMe Read error");
            }

            byte* src = (byte*)_controller.DmaBufferVirt;
            Span<byte> dst = data.Slice((int)i * sector, sector);
            for (int j = 0; j < sector; j++)
            {
                dst[j] = src[j];
            }
        }
    }

    public override void WriteBlock(ulong blockNo, ulong blockCount, Span<byte> data)
    {
        int sector = (int)BlockSize;
        for (ulong i = 0; i < blockCount; i++)
        {
            byte* dst = (byte*)_controller.DmaBufferVirt;
            Span<byte> src = data.Slice((int)i * sector, sector);
            for (int j = 0; j < sector; j++)
            {
                dst[j] = src[j];
            }

            uint sc = _controller.SubmitIo(
                NVMeIoOp.Write,
                _nsid,
                _controller.DmaBufferPhys,
                blockNo + i,
                numLogicalBlocksMinusOne: 0);
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
