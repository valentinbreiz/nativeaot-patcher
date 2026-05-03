// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Logging;

namespace Cosmos.Kernel.Core.X64.Cpu;

/// <summary>
/// X64 interrupt controller - manages IDT and APIC.
/// </summary>
[Logger]
public partial class X64InterruptController : IInterruptController
{
    public bool IsInitialized => ApicManager.IsInitialized;

    public void Initialize()
    {
        Log.Info("Starting IDT initialization");
        Idt.RegisterAllInterrupts();
        Log.Info("IDT initialization complete");
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

    public bool HandleFatalException(ulong interrupt, ulong cpuFlags, ulong faultAddress)
    {
        // x64: Fatal CPU exceptions (0-31) - halt immediately
        if (interrupt <= 31)
        {
            Serial.Write("[INT] FATAL: Exception ", interrupt, "\n");
            Serial.Write("[INT] Error code: 0x");
            Serial.WriteHex(cpuFlags);
            Serial.Write("\n");

            // For page faults (#PF = 14), show the faulting address
            if (interrupt == 14)
            {
                Serial.Write("[INT] Fault address (CR2): 0x");
                Serial.WriteHex(faultAddress);
                Serial.Write("\n");
            }

            while (true) { }
        }
        return false;
    }

    public uint AcknowledgeInterrupt()
    {
        // x64 doesn't use this - interrupt vector comes from IDT directly
        throw new System.NotSupportedException("AcknowledgeInterrupt is not supported on x64. Use IDT vector directly.");
    }
}
