// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.Memory.Heap;
using Cosmos.Kernel.System.IO;

namespace Cosmos.Kernel.HAL.X64.Cpu;

/// <summary>
/// Handles Interrupt Descriptor Table initialization for x86_64.
/// </summary>
public static unsafe partial class Idt
{
    [LibraryImport("*", EntryPoint = "__load_lidt")]
    private static partial void LoadIdt(void* ptr);


    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    private struct IdtEntry
    {

        /// <summary>
        /// Descriptor Privilege Level (DPL)
        /// </summary>
        public enum DescriptorPrivilegeLevelEnum : byte
        {
            Ring0 = 0,
            Ring1 = 1,
            Ring2 = 2,
            Ring3 = 3
        }

        /// <summary>
        /// Gate Type (x86-64)
        /// </summary>
        public enum GateTypeEnum : byte
        {
            TaskGate = 0x5,        // Not used in long mode, but defined
            InterruptGate16 = 0x6, // 16-bit
            TrapGate16 = 0x7,      // 16-bit
            InterruptGate = 0xE,   // 64-bit interrupt gate
            TrapGate = 0xF         // 64-bit trap gate
        }


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
        [FieldOffset(16)]
        public uint Reserved;


        public ulong Offset {
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

    private static IdtEntry[] IdtEntries = new IdtEntry[256];

    /// <summary>
    /// Registers all IRQ stubs in the IDT.
    /// </summary>
    public static void RegisterAllInterrupts()
    {
        Serial.Write("[IDT] start \n");
        for (int i = 0; i < 255; i++)
        {
            IdtEntries[i].Selector = 0;
            Serial.Write("[IDT] Selector ", IdtEntries[i].Selector, "\n");
            IdtEntries[i].RawFlags = 0;
            IdtEntries[i].Offset = (ulong)GetStub(i);
            Serial.Write("[IDT] Offset ", IdtEntries[i].Offset, "\n");
        }

        Serial.Write("[IDT] LoadIdt \n");
        // Load the IDT
        fixed (void* ptr = &IdtEntries[0])
        {
            LoadIdt(&ptr);
        }
        Serial.Write("[IDT] end \n");
    }
    [LibraryImport("*", EntryPoint = "__get_irq_table")]
    private static partial nint GetIrqStub(int index);

    public static delegate* unmanaged<void> GetStub(int index)
    {
        if ((uint)index >= 256)
            throw new ArgumentOutOfRangeException(nameof(index));
        return (delegate* unmanaged<void>)GetIrqStub(index);
    }
}
