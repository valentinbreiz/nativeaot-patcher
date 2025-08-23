using Cosmos.Kernel.Runtime;
using Cosmos.Kernel.System.Input;

namespace Cosmos.Kernel.System.Interrupts;

public static unsafe class InterruptManager
{
    public static void Initialize()
    {
        Idt.SetEntry(0x21, (void*)(delegate* unmanaged<void>)&KeyboardDriver.KeyboardIsr);
        Pic.Initialize();
        Idt.Load();
        Native.Cpu.Sti();
    }

    public static void SendEoi(byte irq) => Pic.SendEoi(irq);
}
