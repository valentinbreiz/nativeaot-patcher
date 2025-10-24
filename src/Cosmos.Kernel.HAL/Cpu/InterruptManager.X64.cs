// This code is licensed under MIT license (see LICENSE for details)


using Cosmos.Kernel.HAL.Cpu.Data;
using Cosmos.Kernel.HAL.X64.Cpu;
using Cosmos.Kernel.System.IO;

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
        Serial.Write("[InterruptManager.Initialize] Starting IDT initialization...\n");
        Idt.RegisterAllInterrupts();
        Serial.Write("[InterruptManager.Initialize] IDT initialization complete\n");
    }

}
