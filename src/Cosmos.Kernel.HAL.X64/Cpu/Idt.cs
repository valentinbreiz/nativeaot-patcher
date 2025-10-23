// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime;
using System.Runtime.CompilerServices;
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

    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 10)]
    private struct IdtPointer
    {
        public ushort Limit;      // Size of IDT - 1 (offset 0-1)
        public ulong Base;        // Base address of IDT (offset 2-9)
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1, Size = 16)]
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
        [FieldOffset(12)]
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
        // Initialize all IDT entries
        for (int i = 0; i < 256; i++)
        {
            IdtEntries[i].Selector = 0x08;  // Kernel code segment
            IdtEntries[i].RawFlags = 0x8E00;  // P=1, DPL=0, Type=Interrupt Gate (1110)
            IdtEntries[i].Offset = (ulong)GetStub(i);

            Serial.Write("[IDT] Selector ", IdtEntries[i].Selector, "\n");
            Serial.Write("[IDT] Offset 0x", IdtEntries[i].Offset.ToString("X"), "\n");
        }

        Serial.Write("[IDT] LoadIdt \n");

        // Get the base address of the IDT array
        fixed (void* ptr = &IdtEntries[0])
        {
            // Create the IDT pointer
            IdtPointer idtPtr = new IdtPointer
            {
                Limit = (ushort)(256 * 16 - 1),  // 256 entries * 16 bytes - 1
                Base = (ulong)ptr
            };

            Serial.Write("[IDT] IDT base: 0x", idtPtr.Base.ToString("X"), "\n");
            Serial.Write("[IDT] IDTR Limit: 0x", ((ulong)idtPtr.Limit).ToString("X"), "\n");
            Serial.Write("[IDT] IDTR Base: 0x", idtPtr.Base.ToString("X"), "\n");

            // Load the IDT
            LoadIdt((void*)&idtPtr);

            Serial.Write("[IDT] LoadIdt called\n");
        }
        
        Serial.Write("[IDT] IDT loaded.\n");
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
