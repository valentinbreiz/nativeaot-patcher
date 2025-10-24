using System.Runtime.InteropServices;

namespace Cosmos.Kernel.HAL.X64.Cpu.Data;

[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 16)]
public struct IdtEntry
{
    [FieldOffset(0)]
    public ushort RawOffsetLow;
    [FieldOffset(2)]
    public ushort Selector;
    [FieldOffset(4)]
    public ushort RawFlags;
    [FieldOffset(6)]
    public ushort RawOffsetMid;
    [FieldOffset(8)]
    public uint RawOffsetHigh;
    [FieldOffset(12)]
    public uint Reserved;

    public ulong Offset
    {
        get
        {
            ulong low = RawOffsetLow;
            ulong mid = RawOffsetMid;
            ulong high = RawOffsetHigh;
            return (high << 32) | (mid << 16) | low;
        }
        set
        {
            RawOffsetLow = (ushort)(value & 0xFFFF);
            RawOffsetMid = (ushort)((value >> 16) & 0xFFFF);
            RawOffsetHigh = (uint)((value >> 32) & 0xFFFFFFFF);
        }
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 10)]
public struct IdtPointer
{
    public ushort Limit;      // Size of IDT - 1
    public ulong Base;        // Base address of IDT
}
