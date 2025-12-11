// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.InteropServices;
using Cosmos.Kernel.HAL.Cpu.Data;
using Cosmos.Kernel.Core.IO;

namespace Cosmos.Kernel.HAL.Cpu;

/// <summary>
/// Provides interrupt registration and dispatch for all architectures.
/// </summary>
public static partial class InterruptManager
{
    [LibraryImport("*", EntryPoint = "__arm64_init_exception_vectors")]
    [SuppressGCTransition]
    private static partial void InitExceptionVectors();

    /// <summary>
    /// Initializes the platform interrupt system (IDT for x86, GIC for ARM).
    /// </summary>
    public static partial void Initialize()
    {
        Serial.Write("[InterruptManager.Initialize] Starting ARM64 exception vector initialization...\n");

        // Initialize exception vectors (VBAR_EL1)
        InitExceptionVectors();
        Serial.Write("[InterruptManager.Initialize] Exception vectors initialized\n");

        // TODO: Implement GIC (Generic Interrupt Controller) for hardware IRQs
        Serial.Write("[InterruptManager.Initialize] ARM64 interrupt system ready\n");
    }
}
