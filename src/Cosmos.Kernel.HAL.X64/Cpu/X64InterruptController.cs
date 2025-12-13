// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.Interfaces;

namespace Cosmos.Kernel.HAL.X64.Cpu;

/// <summary>
/// X64 interrupt controller - manages IDT and APIC.
/// </summary>
public class X64InterruptController : IInterruptController
{
    public bool IsInitialized => ApicManager.IsInitialized;

    public void Initialize()
    {
        Serial.Write("[X64InterruptController] Starting IDT initialization...\n");
        Idt.RegisterAllInterrupts();
        Serial.Write("[X64InterruptController] IDT initialization complete\n");
    }

    public void SendEOI()
    {
        if (ApicManager.IsInitialized)
        {
            ApicManager.SendEOI();
        }
    }

    public void RouteIrq(byte irqNo, byte vector, bool startMasked)
    {
        if (ApicManager.IsInitialized)
        {
            ApicManager.RouteIrq(irqNo, vector, startMasked);
        }
    }

    public bool HandleFatalException(ulong interrupt, ulong cpuFlags)
    {
        // x64: Fatal CPU exceptions (0-31) - halt immediately
        if (interrupt <= 31)
        {
            Serial.Write("[INT] FATAL: Exception ", interrupt, "\n");
            Serial.Write("[INT] Error code: 0x");
            Serial.WriteHex(cpuFlags);
            Serial.Write("\n");
            while (true) { }
        }
        return false;
    }
}
