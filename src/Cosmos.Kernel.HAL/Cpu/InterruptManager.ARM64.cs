// This code is licensed under MIT license (see LICENSE for details)


using Cosmos.Kernel.HAL.Cpu.Data;
using Cosmos.Kernel.Core.IO;

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
        Serial.Write("[InterruptManager.Initialize] Starting ARM64 GIC initialization...\n");
        // TODO: Implement GIC (Generic Interrupt Controller) initialization for ARM64
        Serial.Write("[InterruptManager.Initialize] ARM64 GIC initialization placeholder\n");
    }

}
