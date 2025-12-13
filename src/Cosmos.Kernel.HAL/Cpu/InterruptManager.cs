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
        // Check for fatal exceptions (handled by platform-specific controller)
        if (s_controller != null && ctx.interrupt <= 31)
        {
            if (s_controller.HandleFatalException(ctx.interrupt, ctx.cpu_flags))
            {
                // Controller handled it (likely halted)
                return;
            }
        }

        // For hardware IRQs (vector >= 32), call handler immediately
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

        // Send EOI even for unhandled hardware interrupts to prevent lockup
        if (ctx.interrupt >= 32 && s_controller != null && s_controller.IsInitialized)
        {
            s_controller.SendEOI();
        }
    }
}
