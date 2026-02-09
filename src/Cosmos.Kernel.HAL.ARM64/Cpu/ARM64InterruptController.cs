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

    /// <summary>
    /// The last acknowledged interrupt ID from GIC.
    /// </summary>
    private uint _lastAckedIntId;

    public bool IsInitialized => _initialized;

    public void Initialize()
    {
        Serial.Write("[ARM64InterruptController] Starting exception vector initialization...\n");

        // Initialize exception vectors (VBAR_EL1)
        InitExceptionVectors();
        Serial.Write("[ARM64InterruptController] Exception vectors initialized\n");

        // Initialize the GIC (Generic Interrupt Controller)
        GIC.Initialize();

        // Enable timer interrupts (physical/virtual/hypervisor PPIs since before theyh weren't proper)
        GIC.SetPriority(GIC.TIMER_NONSEC_PHYS, 0x80);  // Medium priority
        GIC.EnableInterrupt(GIC.TIMER_NONSEC_PHYS);
        GIC.SetPriority(GIC.TIMER_VIRT, 0x80);
        GIC.EnableInterrupt(GIC.TIMER_VIRT);
        GIC.SetPriority(GIC.TIMER_HYP, 0x80);
        GIC.EnableInterrupt(GIC.TIMER_HYP);

        Serial.Write("[ARM64InterruptController] ARM64 interrupt system ready\n");

        _initialized = true;
    }

    public void SendEOI()
    {
        // Send End Of Interrupt to GIC
        if (_lastAckedIntId < 1020)  // Valid interrupt ID
        {
            GIC.EndOfInterrupt(_lastAckedIntId);
        }
    }

    /// <summary>
    /// Acknowledges the current interrupt from GIC.
    /// Must be called at the start of IRQ handling.
    /// </summary>
    /// <returns>The interrupt ID, or 1023 if spurious.</returns>
    public uint AcknowledgeInterrupt()
    {
        _lastAckedIntId = GIC.AcknowledgeInterrupt();
        return _lastAckedIntId;
    }

    public void RouteIrq(byte irqNo, byte vector, bool startMasked)
    {
        // On ARM64, irqNo is the GIC interrupt ID
        // Enable the interrupt in GIC
        if (!startMasked)
        {
            GIC.EnableInterrupt(irqNo);
        }

        Serial.Write("[ARM64InterruptController] Routed IRQ ");
        Serial.WriteNumber(irqNo);
        Serial.Write(" -> vector ");
        Serial.WriteNumber(vector);
        Serial.Write("\n");
    }

    /// <summary>
    /// Enables a specific interrupt in the GIC.
    /// </summary>
    /// <param name="intId">Interrupt ID.</param>
    public void EnableInterrupt(uint intId)
    {
        GIC.EnableInterrupt(intId);
    }

    /// <summary>
    /// Disables a specific interrupt in the GIC.
    /// </summary>
    /// <param name="intId">Interrupt ID.</param>
    public void DisableInterrupt(uint intId)
    {
        GIC.DisableInterrupt(intId);
    }

    public bool HandleFatalException(ulong interrupt, ulong cpuFlags, ulong faultAddress)
    {
        // ARM64: During early boot, halt on sync exceptions to prevent infinite recursion
        // interrupt 0 = sync exception from current EL with SPx
        if (interrupt == 0)
        {
            // Decode ESR_EL1 to get exception class
            uint ec = (uint)(cpuFlags >> 26) & 0x3F;
            uint iss = (uint)(cpuFlags & 0x1FFFFFF);

            Serial.Write("[INT] FATAL: Synchronous exception\n");
            Serial.Write("[INT] ESR_EL1: 0x");
            Serial.WriteHex(cpuFlags);
            Serial.Write(" (EC=0x");
            Serial.WriteHex(ec);
            Serial.Write(")\n");
            Serial.Write("[INT] FAR_EL1: 0x");
            Serial.WriteHex(faultAddress);
            Serial.Write("\n");

            // Decode common exception classes
            switch (ec)
            {
                case 0x00: Serial.Write("[INT] Unknown exception\n"); break;
                case 0x15: Serial.Write("[INT] SVC from AArch64\n"); break;
                case 0x20: Serial.Write("[INT] Instruction abort from lower EL\n"); break;
                case 0x21: Serial.Write("[INT] Instruction abort from current EL\n"); break;
                case 0x22: Serial.Write("[INT] PC alignment fault\n"); break;
                case 0x24: Serial.Write("[INT] Data abort from lower EL\n"); break;
                case 0x25: Serial.Write("[INT] Data abort from current EL\n"); break;
                case 0x26: Serial.Write("[INT] SP alignment fault\n"); break;
                default: Serial.Write("[INT] Exception class: 0x"); Serial.WriteHex(ec); Serial.Write("\n"); break;
            }

            Serial.Write("[INT] System halted.\n");
            while (true) { }
        }
        return false;
    }
}
