using System.Runtime.InteropServices;
using Cosmos.Kernel.Runtime;

namespace Cosmos.Kernel.System.Interrupts;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct IdtEntry
{
    public ushort OffsetLow;
    public ushort Selector;
    public byte Ist;
    public byte TypeAttr;
    public ushort OffsetMid;
    public uint OffsetHigh;
    public uint Zero;
}

public static unsafe class Idt
{
    private static readonly IdtEntry[] Entries = new IdtEntry[256];

    public static void SetEntry(int vector, void* handler)
    {
        ulong addr = (ulong)handler;
        Entries[vector] = new IdtEntry
        {
            OffsetLow = (ushort)(addr & 0xFFFF),
            Selector = 0x08,
            Ist = 0,
            TypeAttr = 0x8E,
            OffsetMid = (ushort)((addr >> 16) & 0xFFFF),
            OffsetHigh = (uint)(addr >> 32),
            Zero = 0
        };
    }

    public static void Load()
    {
        fixed (IdtEntry* ptr = Entries)
        {
            Native.Cpu.Lidt(ptr, (ushort)(sizeof(IdtEntry) * Entries.Length - 1));
        }
    }
}
