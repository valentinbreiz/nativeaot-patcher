// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.Core.IO;

namespace Cosmos.Kernel.Core.X64.Cpu;

/// <summary>
/// X64 interrupt controller - manages IDT and APIC, owns the x64 dispatch
/// path (vector lookup, EOI for hardware IRQs, fatal CPU-exception halt).
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

    public void RouteIrq(byte irqNo, byte vector, bool startMasked)
    {
        if (ApicManager.IsInitialized)
        {
            ApicManager.RouteIrq(irqNo, vector, startMasked);
        }
    }

    public void Dispatch(ref IRQContext ctx)
    {
        InterruptManager.IrqDelegate[]? handlers = InterruptManager.s_irqHandlers;
        if (handlers != null && ctx.interrupt < (ulong)handlers.Length)
        {
            InterruptManager.IrqDelegate handler = handlers[(int)ctx.interrupt];
            if (handler != null)
            {
                handler(ref ctx);

                // Send EOI for hardware IRQs (vector >= 32)
                if (ctx.interrupt >= 32 && IsInitialized)
                {
                    SendEOI();
                }
                return;
            }
        }

        // No managed handler - for CPU exceptions (0-31), fall through to fatal halt
        if (ctx.interrupt <= 31)
        {
            HandleFatalException(ctx.interrupt, ctx.cpu_flags, ctx.fault_address);
            return;
        }

        // Send EOI even for unhandled hardware interrupts to prevent lockup
        if (ctx.interrupt >= 32 && IsInitialized)
        {
            SendEOI();
        }
    }

    private static void SendEOI()
    {
        if (ApicManager.IsInitialized)
        {
            ApicManager.SendEOI();
        }
    }

    private static void HandleFatalException(ulong interrupt, ulong cpuFlags, ulong faultAddress)
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
}
