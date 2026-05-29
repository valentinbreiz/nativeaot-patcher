// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core;

namespace Cosmos.Kernel.HAL.Devices.Storage;

/// <summary>NVMe admin opcodes (NVM Express 1.4 §5.x).</summary>
public static class NvmeAdminOp
{
    public const byte DeleteIoSq = 0x00;
    public const byte CreateIoSq = 0x01;
    public const byte DeleteIoCq = 0x04;
    public const byte CreateIoCq = 0x05;
    public const byte Identify = 0x06;
    public const byte SetFeatures = 0x09;
    public const byte GetFeatures = 0x0A;
}

/// <summary>NVMe NVM-command-set IO opcodes (NVM Express 1.4 §6.x).</summary>
public static class NvmeIoOp
{
    public const byte Flush = 0x00;
    public const byte Write = 0x01;
    public const byte Read = 0x02;
}

/// <summary>Identify CNS values (NVM Express 1.4 §5.15).</summary>
public static class NvmeCns
{
    public const byte Namespace = 0x00;
    public const byte Controller = 0x01;
    public const byte ActiveNamespaceList = 0x02;
}

/// <summary>
/// NVMe Submission Queue Entry (64 bytes). Backed by raw memory; the
/// helpers fill the spec-defined CDWs without using managed structs (so
/// the layout stays exactly as the controller expects regardless of
/// runtime padding).
/// </summary>
public unsafe struct NvmeSqe
{
    public ulong Address;

    public NvmeSqe(ulong address)
    {
        Address = address;
        Clear();
    }

    public void Clear()
    {
        ulong* p = (ulong*)Address;
        for (int i = 0; i < 8; i++)
        {
            p[i] = 0;
        }
    }

    public void SetOpcode(byte opcode, ushort cid)
    {
        // CDW0: opcode | FUSE | reserved | PSDT | CID<<16
        uint cdw0 = (uint)opcode | ((uint)cid << 16);
        Native.MMIO.Write32(Address + 0x00, cdw0);
    }

    public void SetNsid(uint nsid)
    {
        Native.MMIO.Write32(Address + 0x04, nsid);
    }

    public void SetPrp1(ulong physAddr)
    {
        Native.MMIO.Write64(Address + 0x18, physAddr);
    }

    public void SetPrp2(ulong physAddr)
    {
        Native.MMIO.Write64(Address + 0x20, physAddr);
    }

    public void SetCdw10(uint value)
    {
        Native.MMIO.Write32(Address + 0x28, value);
    }

    public void SetCdw11(uint value)
    {
        Native.MMIO.Write32(Address + 0x2C, value);
    }

    public void SetCdw12(uint value)
    {
        Native.MMIO.Write32(Address + 0x30, value);
    }
}

/// <summary>
/// NVMe Completion Queue Entry (16 bytes). Reads only — the controller
/// writes these.
/// </summary>
public unsafe struct NvmeCqe
{
    public ulong Address;

    public NvmeCqe(ulong address)
    {
        Address = address;
    }

    public uint CommandSpecific => Native.MMIO.Read32(Address + 0x00);
    public uint Reserved => Native.MMIO.Read32(Address + 0x04);
    public ushort SqHeadPointer => Native.MMIO.Read16(Address + 0x08);
    public ushort SqIdentifier => Native.MMIO.Read16(Address + 0x0A);
    public ushort CommandIdentifier => Native.MMIO.Read16(Address + 0x0C);
    public ushort StatusField => Native.MMIO.Read16(Address + 0x0E);

    /// <summary>Phase tag bit (toggles each pass through the queue).</summary>
    public bool Phase => (StatusField & 0x1) != 0;

    /// <summary>Status code field — 0 means success.</summary>
    public uint StatusCode => (uint)((StatusField >> 1) & 0x7FFF);
}
