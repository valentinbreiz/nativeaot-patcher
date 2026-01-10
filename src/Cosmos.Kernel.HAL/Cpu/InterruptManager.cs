// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.Cpu.Data;
using Cosmos.Kernel.HAL.Interfaces;

namespace Cosmos.Kernel.HAL.Cpu;

/// <summary>
/// Interrupt manager - provides interrupt registration and dispatch for all architectures.
/// </summary>
public static class InterruptManager
{
    /// <summary>
    /// Interrupt delegate signature.
    /// </summary>
    /// <param name="context">The interrupt context captured by the CPU.</param>
    public delegate void IrqDelegate(ref IRQContext context);

    internal static IrqDelegate[]? s_irqHandlers;
    private static IInterruptController? s_controller;

    private const string NewLine = "\n";

    /// <summary>
    /// Initializes the interrupt manager with a platform-specific controller.
    /// </summary>
    /// <param name="controller">Platform-specific interrupt controller (X64 or ARM64).</param>
    public static void Initialize(IInterruptController controller)
    {
        Serial.Write("[InterruptManager.Initialize] Allocating handlers array...\n");
        s_irqHandlers = new IrqDelegate[256];
        s_controller = controller;

        Serial.Write("[InterruptManager.Initialize] Initializing platform interrupt controller...\n");
        controller.Initialize();
        Serial.Write("[InterruptManager.Initialize] Interrupt system ready\n");
    }

    /// <summary>
    /// Registers a handler for an interrupt vector.
    /// </summary>
    /// <param name="vector">Interrupt vector index.</param>
    /// <param name="handler">Delegate to handle the interrupt.</param>
    public static void SetHandler(byte vector, IrqDelegate handler)
    {
        if (s_irqHandlers == null)
        {
            Serial.Write("[InterruptManager] ERROR: s_irqHandlers is null! Initialize() must be called first.\n");
            return;
        }
        s_irqHandlers[vector] = handler;
    }

    /// <summary>
    /// Registers a handler for a hardware IRQ and routes it through the interrupt controller.
    /// </summary>
    /// <param name="irqNo">IRQ index (0-15 for ISA IRQs).</param>
    /// <param name="handler">IRQ handler delegate.</param>
    /// <param name="startMasked">If true, the IRQ starts masked and must be explicitly unmasked.</param>
    public static void SetIrqHandler(byte irqNo, IrqDelegate handler, bool startMasked = false)
    {
        byte vector = (byte)(0x20 + irqNo);
        SetHandler(vector, handler);

        // Route the IRQ through the platform-specific controller
        if (s_controller != null && s_controller.IsInitialized)
        {
            Serial.Write("[InterruptManager] Routing IRQ ", irqNo, " -> vector 0x", vector.ToString("X"), NewLine);
            s_controller.RouteIrq(irqNo, vector, startMasked);
        }
    }

    /// <summary>
    /// Called by native bridge from ASM stubs to invoke the proper handler.
    /// </summary>
    /// <param name="ctx">Context structure.</param>
    public static void Dispatch(ref IRQContext ctx)
    {
#if ARCH_ARM64
        // ARM64: Handle different exception types
        // interrupt 0 = Synchronous exception (faults, SVC, etc.)
        // interrupt 1 = IRQ (hardware interrupts via GIC)
        // interrupt 2 = FIQ (fast interrupts, not used)
        // interrupt 3 = SError (system error)

        if (ctx.interrupt == 1)  // IRQ from GIC
        {
            // Acknowledge the interrupt from GIC to get the actual interrupt ID
            if (s_controller != null)
            {
                uint intId = s_controller.AcknowledgeInterrupt();

                // Check for spurious interrupt
                if (intId >= 1020)
                {
                    // Spurious interrupt - no EOI needed
                    return;
                }

                // Dispatch to handler based on GIC interrupt ID
                if (s_irqHandlers != null && intId < (uint)s_irqHandlers.Length)
                {
                    IrqDelegate handler = s_irqHandlers[(int)intId];
                    if (handler != null)
                    {
                        handler(ref ctx);
                    }
                }

                // Send EOI
                s_controller.SendEOI();
            }
            return;
        }
        else if (ctx.interrupt == 0)  // Synchronous exception
        {
            // Check for managed handler (for SVC, etc.)
            if (s_irqHandlers != null)
            {
                IrqDelegate handler = s_irqHandlers[0];
                if (handler != null)
                {
                    handler(ref ctx);
                    return;
                }
            }

            // No handler - use fatal exception handler
            if (s_controller != null)
            {
                s_controller.HandleFatalException(ctx.interrupt, ctx.cpu_flags, ctx.far);
            }
            return;
        }
        else
        {
            // FIQ or SError - log and halt
            Serial.Write("[INT] Unexpected exception type: ", ctx.interrupt, NewLine);
            if (s_controller != null)
            {
                s_controller.HandleFatalException(ctx.interrupt, ctx.cpu_flags, ctx.far);
            }
            return;
        }
#else
        // x64: Original behavior
        // First check if there's a registered managed handler
        if (s_irqHandlers != null && ctx.interrupt < (ulong)s_irqHandlers.Length)
        {
            IrqDelegate handler = s_irqHandlers[(int)ctx.interrupt];
            if (handler != null)
            {
                handler(ref ctx);

                // Send EOI for hardware IRQs (vector >= 32)
                if (ctx.interrupt >= 32 && s_controller != null && s_controller.IsInitialized)
                {
                    s_controller.SendEOI();
                }
                return;
            }
        }

        // No managed handler - for CPU exceptions (0-31), use fallback fatal handler
        if (s_controller != null && ctx.interrupt <= 31)
        {
            ulong faultAddress = ctx.cr2;
            s_controller.HandleFatalException(ctx.interrupt, ctx.cpu_flags, faultAddress);
            // HandleFatalException halts, so we won't reach here
            return;
        }

        // Send EOI even for unhandled hardware interrupts to prevent lockup
        if (ctx.interrupt >= 32 && s_controller != null && s_controller.IsInitialized)
        {
            s_controller.SendEOI();
        }
#endif
    }
}
