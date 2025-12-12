// This code is licensed under MIT license (see LICENSE for details)


using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Runtime;
using Cosmos.Kernel.HAL.Cpu.Data;
#if ARCH_X64
using Cosmos.Kernel.HAL.X64.Cpu;
#endif

namespace Cosmos.Kernel.HAL.Cpu;

/// <summary>
/// Provides interrupt registration and dispatch for all architectures.
/// </summary>
public static partial class InterruptManager
{
    /// <summary>
    /// Interrupt delegate signature.
    /// </summary>
    /// <param name="context">The interrupt context captured by the CPU.</param>
    public delegate void IrqDelegate(ref IRQContext context);

    internal static IrqDelegate[]? s_irqHandlers;

    /// <summary>
    /// Initializes the platform interrupt system (IDT for x86, GIC for ARM).
    /// </summary>
    public static partial void Initialize();

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

        // Route the IRQ through the APIC
#if ARCH_X64
        if (ApicManager.IsInitialized)
        {
            Serial.Write("[InterruptManager] Routing IRQ ", irqNo, " -> vector 0x", vector.ToString("X"), NewLine);
            ApicManager.RouteIrq(irqNo, vector, startMasked);
        }
#endif
    }

    private const string NewLine = "\n";

    /// <summary>
    /// Called by native bridge from ASM stubs to invoke the proper handler.
    /// </summary>
    /// <param name="ctx">Context structure.</param>
    public static void Dispatch(ref IRQContext ctx)
    {
#if ARCH_ARM64
        // During early boot, halt on sync exceptions to prevent infinite recursion
        if (ctx.interrupt == 0 || ctx.interrupt == 3)
        {
            Serial.Write("[INT] FATAL: Unhandled ARM64 exception type ", ctx.interrupt, NewLine);
            Serial.Write("[INT] ESR_EL1: 0x", ctx.cpu_flags.ToString("X"), NewLine);
            Serial.Write("[INT] ELR_EL1: 0x", ctx.elr.ToString("X"), NewLine);
            Serial.Write("[INT] FAR_EL1: 0x", ctx.far.ToString("X"), NewLine);
            Serial.Write("[INT] System halted.\n");
            while (true) { }
        }
#else
        // x64: Fatal CPU exceptions (0-31) - halt immediately
        if (ctx.interrupt <= 31)
        {
            Serial.Write("[INT] FATAL: Exception ", ctx.interrupt, NewLine);
            Serial.Write("[INT] cr2=0x");
            Serial.WriteHex(ctx.cr2);
            Serial.Write(NewLine);
            while (true) { }
        }
#endif

        // For hardware IRQs (vector >= 32), call handler immediately without debug spam
        if (s_irqHandlers != null && ctx.interrupt < (ulong)s_irqHandlers.Length)
        {
            IrqDelegate handler = s_irqHandlers[(int)ctx.interrupt];
            if (handler != null)
            {
                handler(ref ctx);

                // Send EOI for hardware IRQs (vector >= 32)
#if ARCH_X64
                if (ctx.interrupt >= 32 && ApicManager.IsInitialized)
                {
                    ApicManager.SendEOI();
                }
#endif
                return;
            }
        }

        // Send EOI even for unhandled hardware interrupts to prevent lockup
#if ARCH_X64
        if (ctx.interrupt >= 32 && ApicManager.IsInitialized)
        {
            ApicManager.SendEOI();
        }
#endif
    }
}
