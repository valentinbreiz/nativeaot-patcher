// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.InteropServices;

namespace Cosmos.Kernel.HAL.Cpu.Data;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct IdtEntry
{
    public ushort OffsetLow;
    public ushort Selector;
    public byte Ist;
    public byte TypeAttr;
    public ushort OffsetMid;
    public uint OffsetHigh;
    public uint Reserved;

    public void SetHandler(nuint handler)
    {
        OffsetLow = (ushort)(handler & 0xFFFF);
        Selector = 0x08;      // kernel code segment (must match your GDT)
        Ist = 0;
        TypeAttr = 0x8E;      // present, DPL=0, type=0xE (interrupt gate)
        OffsetMid = (ushort)((handler >> 16) & 0xFFFF);
        OffsetHigh = (uint)((handler >> 32) & 0xFFFFFFFF);
        Reserved = 0;
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct IdtPointer
{
    public ushort Limit;
    public nuint Base;
}
