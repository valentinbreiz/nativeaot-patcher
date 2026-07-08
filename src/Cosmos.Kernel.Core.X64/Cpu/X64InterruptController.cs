// This code is licensed under MIT license (see LICENSE for details)

using System.Runtime.CompilerServices;
using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Scheduler;

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

    public unsafe void Dispatch(ref IRQContext ctx)
    {
        InterruptManager.IrqDelegate[]? handlers = InterruptManager.s_irqHandlers;
        if (handlers != null && ctx.interrupt < (ulong)handlers.Length)
        {
            InterruptManager.IrqDelegate handler = handlers[(int)ctx.interrupt];
            if (handler != null)
            {
                handler(ref ctx);

                // Send EOI for hardware IRQs (vector >= 32) — but never for
                // the APIC spurious vector: a spurious delivery sets no ISR
                // bit, so an EOI here would retire whichever real interrupt
                // is currently in service (SDM 3A §11.9).
                if (ctx.interrupt >= 32 && ctx.interrupt != LocalApic.SPURIOUS_VECTOR && IsInitialized)
                {
                    SendEOI();

                    // A handler-side ReadyThread (e.g. InterruptEvent.Signal
                    // from a device ISR) requests a reschedule; honor it now —
                    // the common asm stub applies the staged context switch on
                    // every interrupt exit, not just timer ticks. Same RSP
                    // derivation (and kernel-space sanity check) as the LAPIC
                    // timer handler: the saved context sits 256 bytes (XMM
                    // save area) below the IRQContext.
                    nuint currentRsp = (nuint)Unsafe.AsPointer(ref ctx) - 256;
                    if ((currentRsp & 0xFFFF000000000000) == 0xFFFF000000000000)
                    {
                        SchedulerManager.ReschedulePendingFromIrq(LocalApic.GetId(), currentRsp);
                    }
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

        // Send EOI even for unhandled hardware interrupts to prevent lockup.
        // The spurious vector is the exception (see above): it arrives here
        // because nothing registers a handler for it, and it must be
        // dismissed without EOI.
        if (ctx.interrupt >= 32 && ctx.interrupt != LocalApic.SPURIOUS_VECTOR && IsInitialized)
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
