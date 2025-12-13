// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.Interfaces;

namespace Cosmos.Kernel.HAL.ARM64.Cpu;

/// <summary>
/// ARM64 interrupt controller - manages exception vectors and GIC.
/// </summary>
public partial class ARM64InterruptController : IInterruptController
{
    [LibraryImport("*", EntryPoint = "_native_arm64_init_exception_vectors")]
    [SuppressGCTransition]
    private static partial void InitExceptionVectors();

    private bool _initialized;

    public bool IsInitialized => _initialized;

    public void Initialize()
    {
        Serial.Write("[ARM64InterruptController] Starting exception vector initialization...\n");

        // Initialize exception vectors (VBAR_EL1)
        InitExceptionVectors();
        Serial.Write("[ARM64InterruptController] Exception vectors initialized\n");

        // TODO: Implement GIC (Generic Interrupt Controller) for hardware IRQs
        Serial.Write("[ARM64InterruptController] ARM64 interrupt system ready\n");

        _initialized = true;
    }

    public void SendEOI()
    {
        // TODO: Implement GIC EOI when GIC is supported
    }

    public void RouteIrq(byte irqNo, byte vector, bool startMasked)
    {
        // TODO: Implement GIC IRQ routing when GIC is supported
    }

    public bool HandleFatalException(ulong interrupt, ulong cpuFlags)
    {
        // ARM64: During early boot, halt on sync exceptions to prevent infinite recursion
        // interrupt 0 = sync exception from current EL with SP_EL0
        // interrupt 3 = sync exception from lower EL (EL0)
        if (interrupt == 0 || interrupt == 3)
        {
            Serial.Write("[INT] FATAL: Unhandled ARM64 exception type ", interrupt, "\n");
            Serial.Write("[INT] ESR_EL1: 0x", cpuFlags.ToString("X"), "\n");
            Serial.Write("[INT] System halted.\n");
            while (true) { }
        }
        return false;
    }
}
