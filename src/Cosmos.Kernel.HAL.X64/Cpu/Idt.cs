// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.X64.Cpu.Data;

namespace Cosmos.Kernel.HAL.X64.Cpu;

/// <summary>
/// Handles Interrupt Descriptor Table initialization for x86_64.
/// </summary>
public static unsafe partial class Idt
{
    [LibraryImport("*", EntryPoint = "_native_x64_load_idt")]
    [SuppressGCTransition]
    private static partial void LoadIdt(void* ptr);

    [LibraryImport("*", EntryPoint = "_native_x64_get_code_selector")]
    [SuppressGCTransition]
    public static partial ulong GetCurrentCodeSelector();

    private static IdtEntry[]? IdtEntries;

    /// <summary>
    /// Registers all IRQ stubs in the IDT.
    /// </summary>
    public static void RegisterAllInterrupts()
    {
        // Get the current code selector from the bootloader (Limine)
        ulong cs = GetCurrentCodeSelector();
        Serial.Write("[IDT] Initializing with CS=0x", cs.ToString("X"), "\n");

        // Allocate IDT entries array if not already done
        if (IdtEntries == null)
        {
            Serial.WriteString("[IDT] Allocating IdtEntries array...\n");
            IdtEntries = new IdtEntry[256];
        }
        Serial.WriteString("[IDT] IdtEntries length: ");
        Serial.WriteNumber((ulong)IdtEntries.Length);
        Serial.WriteString("\n");

        // Initialize all 256 IDT entries
        for (int i = 0; i < 256; i++)
        {
            if (i == 0 || i == 255)
            {
                Serial.WriteString("[IDT] Setting entry ");
                Serial.WriteNumber((ulong)i);
                Serial.WriteString("\n");
            }
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

    [LibraryImport("*", EntryPoint = "_native_x64_get_irq_stub")]
    [SuppressGCTransition]
    private static partial nint GetIrqStub(int index);

    public static delegate* unmanaged<void> GetStub(int index)
    {
        if ((uint)index >= 256)
            throw new ArgumentOutOfRangeException(nameof(index));
        nint stubAddr = GetIrqStub(index);

        return (delegate* unmanaged<void>)stubAddr;
    }
}
