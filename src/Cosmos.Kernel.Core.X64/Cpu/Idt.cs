// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core.Logging;
using Cosmos.Kernel.Core.X64.Bridge;
using Cosmos.Kernel.Core.X64.Cpu.Data;

namespace Cosmos.Kernel.Core.X64.Cpu;

/// <summary>
/// Handles Interrupt Descriptor Table initialization for x86_64.
/// Native imports live in Cosmos.Kernel.Core.X64/Bridge/Import/IdtNative.cs.
/// </summary>
[Logger(Category = "IDT")]
public static unsafe partial class Idt
{
    public static ulong GetCurrentCodeSelector() => IdtNative.GetCurrentCodeSelector();

    private static IdtEntry[]? IdtEntries;

    /// <summary>
    /// Registers all IRQ stubs in the IDT.
    /// </summary>
    public static void RegisterAllInterrupts()
    {
        // Get the current code selector from the bootloader (Limine)
        ulong cs = GetCurrentCodeSelector();
        Log.Debug($"Initializing with CS=0x{cs:X}");

        // Allocate IDT entries array if not already done
        if (IdtEntries == null)
        {
            Log.Debug("Allocating IdtEntries array");
            IdtEntries = new IdtEntry[256];
        }
        Log.Debug($"IdtEntries length: {IdtEntries.Length}");

        // Initialize all 256 IDT entries
        for (int i = 0; i < 256; i++)
        {
            if (i == 0 || i == 255)
            {
                Log.Debug($"Setting entry {i}");
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

            Log.Debug($"IDT base: 0x{idtPtr.Base:X}");
            Log.Debug($"IDT limit: 0x{idtPtr.Limit:X}");

            IdtNative.LoadIdt((void*)&idtPtr);
            Log.Debug($"Loaded 256 vectors at 0x{idtPtr.Base:X}");
        }

        Log.Info("IDT loaded");
    }

    public static delegate* unmanaged<void> GetStub(int index)
    {
        if ((uint)index >= 256)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        nint stubAddr = IdtNative.GetIrqStub(index);

        return (delegate* unmanaged<void>)stubAddr;
    }
}
