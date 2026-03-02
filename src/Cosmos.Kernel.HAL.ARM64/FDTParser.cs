// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.CompilerServices;
using Cosmos.Kernel.Core.IO;

namespace Cosmos.Kernel.HAL.ARM64;

/// <summary>
/// Minimal Flattened Device Tree (FDT) parser for extracting GIC base addresses.
/// Parses the DTB binary format (big-endian) to find the interrupt-controller node
/// and read its reg property for GICD/GICR/GICC addresses.
/// </summary>
public static unsafe class FDTParser
{
    // FDT magic number
    private const uint FDT_MAGIC = 0xD00DFEED;

    // FDT tokens
    private const uint FDT_BEGIN_NODE = 1;
    private const uint FDT_END_NODE = 2;
    private const uint FDT_PROP = 3;
    private const uint FDT_NOP = 4;
    private const uint FDT_END = 9;

    /// <summary>
    /// Parsed GIC information from the device tree.
    /// </summary>
    public struct GICInfo
    {
        public bool Found;
        public byte Version;       // 2 or 3
        public ulong DistBase;     // GICD base address
        public ulong DistSize;     // GICD region size
        public ulong CpuBase;      // GICC base (v2) or GICR base (v3)
        public ulong CpuSize;      // GICC/GICR region size
        public ulong HypBase;      // GICH base (v2) or GITS base (v3), optional
        public ulong HypSize;
        public ulong VcpuBase;     // GICV base (v2) or additional region (v3), optional
        public ulong VcpuSize;
    }

    /// <summary>
    /// Parses a Flattened Device Tree blob to find GIC base addresses.
    /// </summary>
    /// <param name="dtbAddress">Pointer to the DTB in memory.</param>
    /// <returns>GIC information if found.</returns>
    public static GICInfo ParseGIC(void* dtbAddress)
    {
        GICInfo info = default;

        if (dtbAddress == null)
        {
            Serial.Write("[FDT] No DTB provided\n");
            return info;
        }

        byte* dtb = (byte*)dtbAddress;

        // Validate magic
        uint magic = ReadBE32(dtb, 0);
        if (magic != FDT_MAGIC)
        {
            Serial.Write("[FDT] Invalid DTB magic: 0x");
            Serial.WriteHex(magic);
            Serial.Write("\n");
            return info;
        }

        uint totalSize = ReadBE32(dtb, 4);
        uint offStructs = ReadBE32(dtb, 8);
        uint offStrings = ReadBE32(dtb, 12);
        // uint offMemRsvMap = ReadBE32(dtb, 16); // not needed
        uint version = ReadBE32(dtb, 20);

        Serial.Write("[FDT] DTB version ");
        Serial.WriteNumber(version);
        Serial.Write(", size ");
        Serial.WriteNumber(totalSize);
        Serial.Write(" bytes\n");

        byte* structs = dtb + offStructs;
        byte* strings = dtb + offStrings;
        uint structsLen = totalSize - offStructs;

        // Walk the structure block looking for the GIC node
        WalkStructureForGIC(structs, structsLen, strings, ref info);

        if (info.Found)
        {
            Serial.Write("[FDT] GIC found: v");
            Serial.WriteNumber(info.Version);
            Serial.Write(" GICD=0x");
            Serial.WriteHex(info.DistBase);
            if (info.Version >= 3)
            {
                Serial.Write(" GICR=0x");
                Serial.WriteHex(info.CpuBase);
            }
            else
            {
                Serial.Write(" GICC=0x");
                Serial.WriteHex(info.CpuBase);
            }
            Serial.Write("\n");
        }
        else
        {
            Serial.Write("[FDT] GIC node not found in DTB\n");
        }

        return info;
    }

    private static void WalkStructureForGIC(byte* structs, uint structsLen, byte* strings, ref GICInfo info)
    {
        uint offset = 0;
        int depth = 0;
        bool inGICNode = false;
        int gicNodeDepth = -1;
        uint addressCells = 2; // default #address-cells
        uint sizeCells = 2;    // default #size-cells
        // Track parent's address/size cells for the GIC reg interpretation
        uint parentAddressCells = 2;
        uint parentSizeCells = 2;

        // Temporary storage for the GIC node's "reg" property
        byte* regData = null;
        uint regLen = 0;
        bool isV3 = false;

        while (offset < structsLen)
        {
            uint token = ReadBE32(structs, offset);
            offset += 4;

            switch (token)
            {
                case FDT_BEGIN_NODE:
                {
                    // Node name follows (null-terminated, 4-byte aligned)
                    uint nameStart = offset;
                    while (offset < structsLen && structs[offset] != 0)
                        offset++;
                    uint nameLen = offset - nameStart;
                    offset++; // skip null
                    offset = Align4(offset);

                    if (!inGICNode)
                    {
                        // Save parent address/size cells before entering child
                        parentAddressCells = addressCells;
                        parentSizeCells = sizeCells;

                        // Check if this is a GIC-like node name
                        if (IsGICNodeName(structs + nameStart, nameLen))
                        {
                            inGICNode = true;
                            gicNodeDepth = depth;
                        }
                    }

                    depth++;
                    break;
                }

                case FDT_END_NODE:
                {
                    depth--;
                    if (inGICNode && depth == gicNodeDepth)
                    {
                        // Leaving the GIC node - process what we found
                        if (regData != null && regLen > 0)
                        {
                            ParseGICReg(regData, regLen, parentAddressCells, parentSizeCells, isV3, ref info);
                        }
                        inGICNode = false;
                        gicNodeDepth = -1;
                    }
                    break;
                }

                case FDT_PROP:
                {
                    uint propLen = ReadBE32(structs, offset);
                    offset += 4;
                    uint nameOff = ReadBE32(structs, offset);
                    offset += 4;
                    uint dataStart = offset;
                    offset += propLen;
                    offset = Align4(offset);

                    // Get property name from strings block
                    byte* propName = strings + nameOff;

                    if (inGICNode && depth == gicNodeDepth + 1)
                    {
                        // Properties at the GIC node level
                        if (StringEquals(propName, "compatible"))
                        {
                            // Check compatible string for v2 vs v3
                            if (ContainsString(structs + dataStart, propLen, "arm,gic-v3"))
                            {
                                isV3 = true;
                            }
                            else if (ContainsString(structs + dataStart, propLen, "arm,cortex-a15-gic") ||
                                     ContainsString(structs + dataStart, propLen, "arm,gic-400"))
                            {
                                isV3 = false;
                            }
                        }
                        else if (StringEquals(propName, "reg"))
                        {
                            regData = structs + dataStart;
                            regLen = propLen;
                        }
                        else if (StringEquals(propName, "#address-cells"))
                        {
                            addressCells = ReadBE32(structs, dataStart);
                        }
                        else if (StringEquals(propName, "#size-cells"))
                        {
                            sizeCells = ReadBE32(structs, dataStart);
                        }
                    }
                    else if (!inGICNode && depth <= 1)
                    {
                        // Track root-level address/size cells
                        if (StringEquals(propName, "#address-cells"))
                        {
                            addressCells = ReadBE32(structs, dataStart);
                            parentAddressCells = addressCells;
                        }
                        else if (StringEquals(propName, "#size-cells"))
                        {
                            sizeCells = ReadBE32(structs, dataStart);
                            parentSizeCells = sizeCells;
                        }
                    }
                    break;
                }

                case FDT_NOP:
                    break;

                case FDT_END:
                    return;

                default:
                    return; // Unknown token
            }
        }
    }

    private static void ParseGICReg(byte* regData, uint regLen, uint addressCells, uint sizeCells, bool isV3, ref GICInfo info)
    {
        uint cellSize = (addressCells + sizeCells) * 4;
        if (cellSize == 0 || regLen < cellSize)
            return;

        uint entryCount = regLen / cellSize;
        uint off = 0;

        info.Found = true;
        info.Version = isV3 ? (byte)3 : (byte)2;

        // Entry 0: Distributor (GICD)
        if (entryCount >= 1)
        {
            info.DistBase = ReadAddress(regData, ref off, addressCells);
            info.DistSize = ReadAddress(regData, ref off, sizeCells);
        }

        // Entry 1: CPU interface (GICC for v2) or Redistributor (GICR for v3)
        if (entryCount >= 2)
        {
            info.CpuBase = ReadAddress(regData, ref off, addressCells);
            info.CpuSize = ReadAddress(regData, ref off, sizeCells);
        }

        // Entry 2: Hypervisor interface (GICH for v2) or GITS for v3
        if (entryCount >= 3)
        {
            info.HypBase = ReadAddress(regData, ref off, addressCells);
            info.HypSize = ReadAddress(regData, ref off, sizeCells);
        }

        // Entry 3: Virtual CPU interface (GICV for v2) or additional for v3
        if (entryCount >= 4)
        {
            info.VcpuBase = ReadAddress(regData, ref off, addressCells);
            info.VcpuSize = ReadAddress(regData, ref off, sizeCells);
        }
    }

    private static ulong ReadAddress(byte* data, ref uint offset, uint cells)
    {
        ulong value = 0;
        for (uint i = 0; i < cells; i++)
        {
            value = (value << 32) | ReadBE32(data, offset);
            offset += 4;
        }
        return value;
    }

    private static bool IsGICNodeName(byte* name, uint len)
    {
        // GIC nodes are typically named "intc@...", "gic@...", "interrupt-controller@..."
        // We check for common prefixes
        if (len >= 4 && name[0] == 'g' && name[1] == 'i' && name[2] == 'c')
            return true;

        if (len >= 4 && name[0] == 'i' && name[1] == 'n' && name[2] == 't' && name[3] == 'c')
            return true;

        if (len >= 21)
        {
            // "interrupt-controller@"
            byte* pattern = stackalloc byte[] {
                (byte)'i', (byte)'n', (byte)'t', (byte)'e', (byte)'r', (byte)'r', (byte)'u',
                (byte)'p', (byte)'t', (byte)'-', (byte)'c', (byte)'o', (byte)'n', (byte)'t',
                (byte)'r', (byte)'o', (byte)'l', (byte)'l', (byte)'e', (byte)'r'
            };
            bool match = true;
            for (int i = 0; i < 20; i++)
            {
                if (name[i] != pattern[i])
                {
                    match = false;
                    break;
                }
            }
            if (match) return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ReadBE32(byte* data, uint offset)
    {
        return (uint)(data[offset] << 24 | data[offset + 1] << 16 | data[offset + 2] << 8 | data[offset + 3]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Align4(uint offset)
    {
        return (offset + 3) & ~3u;
    }

    private static bool StringEquals(byte* a, string b)
    {
        for (int i = 0; i < b.Length; i++)
        {
            if (a[i] != (byte)b[i])
                return false;
        }
        return a[b.Length] == 0;
    }

    private static bool ContainsString(byte* data, uint len, string target)
    {
        // FDT compatible strings are null-separated lists
        uint i = 0;
        while (i < len)
        {
            uint start = i;
            while (i < len && data[i] != 0)
                i++;
            uint strLen = i - start;

            if (strLen == (uint)target.Length)
            {
                bool match = true;
                for (int j = 0; j < target.Length; j++)
                {
                    if (data[start + j] != (byte)target[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                    return true;
            }
            i++; // skip null
        }
        return false;
    }
}
