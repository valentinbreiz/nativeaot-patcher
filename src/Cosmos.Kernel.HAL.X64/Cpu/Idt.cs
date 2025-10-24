// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.Memory.Heap;
using Cosmos.Kernel.HAL;
using Cosmos.Kernel.HAL.X64.Cpu.Data;
using Cosmos.Kernel.System.IO;

namespace Cosmos.Kernel.HAL.X64.Cpu;

/// <summary>
/// Handles Interrupt Descriptor Table initialization for x86_64.
/// </summary>
public static unsafe partial class Idt
{
    [LibraryImport("*", EntryPoint = "__load_lidt")]
    private static partial void LoadIdt(void* ptr);

    [LibraryImport("*", EntryPoint = "__get_current_code_selector")]
    private static partial ulong GetCurrentCodeSelector();

    private static IdtEntry[] IdtEntries = new IdtEntry[256];

    /// <summary>
    /// Registers all IRQ stubs in the IDT.
    /// </summary>
    public static void RegisterAllInterrupts()
    {
        // Get the current code selector from the bootloader (Limine)
        ulong cs = GetCurrentCodeSelector();
        Serial.Write("[IDT] Initializing with CS=0x", cs.ToString("X"), "\n");

        // Initialize all 256 IDT entries
        for (int i = 0; i < 256; i++)
        {
            IdtEntries[i].Selector = (ushort)cs;
            IdtEntries[i].RawFlags = 0x8E00;  // P=1, DPL=0, Type=Interrupt Gate
            IdtEntries[i].Offset = (ulong)GetStub(i);
        }

        // Load the IDT into the CPU
        fixed (void* ptr = &IdtEntries[0])
        {
            IdtPointer idtPtr = new IdtPointer
            {
                Limit = (ushort)(256 * 16 - 1),
                Base = (ulong)ptr
            };

            Serial.Write("[IDT] IDT base: 0x", idtPtr.Base.ToString("X"), "\n");
            Serial.Write("[IDT] IDT Limit: 0x", ((ulong)idtPtr.Limit).ToString("X"), "\n");

            // Load the IDT
            LoadIdt((void*)&idtPtr);
            Serial.Write("[IDT] Loaded 256 interrupt vectors at 0x", idtPtr.Base.ToString("X"), "\n");
        }

        Serial.Write("[IDT] IDT loaded.\n");
    }

    [LibraryImport("*", EntryPoint = "__get_irq_table")]
    private static partial nint GetIrqStub(int index);

    public static delegate* unmanaged<void> GetStub(int index)
    {
        if ((uint)index >= 256)
            throw new ArgumentOutOfRangeException(nameof(index));
        nint stubAddr = GetIrqStub(index);

        return (delegate* unmanaged<void>)stubAddr;
    }
}
