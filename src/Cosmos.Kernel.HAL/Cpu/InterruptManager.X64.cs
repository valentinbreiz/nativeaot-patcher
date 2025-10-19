// This code is licensed under MIT license (see LICENSE for details)


using Cosmos.Kernel.HAL.Cpu.Data;
using Cosmos.Kernel.HAL.X64.Cpu;

namespace Cosmos.Kernel.HAL.Cpu;

/// <summary>
/// Provides interrupt registration and dispatch for all architectures.
/// </summary>
public static partial class InterruptManager
{

    /// <summary>
    /// Initializes the platform interrupt system (IDT for x86, GIC for ARM).
    /// </summary>
    public static partial void Initialize()
    {
        s_irqHandlers =  new IrqDelegate[256];
        Idt.RegisterAllInterrupts();
    }

}
