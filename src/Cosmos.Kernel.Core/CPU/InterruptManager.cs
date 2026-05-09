// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core.IO;

namespace Cosmos.Kernel.Core.CPU;

/// <summary>
/// Interrupt manager - provides interrupt registration and dispatch for all architectures.
/// Lives in Core because the vector table and dispatch logic are hardware-neutral;
/// arch-specific drivers implement <see cref="IInterruptController"/> in
/// Cosmos.Kernel.HAL.X64 / Cosmos.Kernel.HAL.ARM64.
/// </summary>
public static class InterruptManager
{
    /// <summary>
    /// Interrupt delegate signature.
    /// </summary>
    /// <param name="context">The interrupt context captured by the CPU.</param>
    public delegate void IrqDelegate(ref IRQContext context);

    internal static IrqDelegate[]? s_irqHandlers;

    // ARM64 LPIs (GICv3 ITS) live in INTID space starting at 8192. The
    // dense s_irqHandlers[256] table can't index them — we keep a separate
    // sparse array sized to the LPI window the kernel commits to. 1024
    // covers many devices' worth of MSI-X vectors and matches the default
    // PROPBASER allocation in GICv3Lpi.
    internal const uint LpiBase = 8192;
    internal const int LpiCount = 1024;
    internal static IrqDelegate[]? s_lpiHandlers;
    private static int s_nextLpiOffset;

    private static IInterruptController? s_controller;

    private const string NewLine = "\n";

    /// <summary>
    /// Whether interrupt support is enabled. Uses centralized feature flag.
    /// </summary>
    public static bool IsEnabled => CosmosFeatures.InterruptsEnabled;

    /// <summary>
    /// Initializes the interrupt manager with a platform-specific controller.
    /// </summary>
    /// <param name="controller">Platform-specific interrupt controller (X64 or ARM64).</param>
    public static void Initialize(IInterruptController controller)
    {
        Serial.Write("[InterruptManager.Initialize] Allocating handlers array...\n");
        s_irqHandlers = new IrqDelegate[256];
        s_lpiHandlers = new IrqDelegate[LpiCount];
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

    // Dynamic vector allocations (MSI / MSI-X) start above the legacy
    // ISA-IRQ window (0x20–0x2F) and any future arch-reserved range
    // (0x30–0x3F), and stop one short of the APIC spurious vector 0xFF.
    private const byte DynamicVectorMin = 0x40;
    private const byte DynamicVectorMax = 0xFE;
    private static int s_nextDynamicVector = DynamicVectorMin;

    // Guards s_irqHandlers / s_lpiHandlers RMW in AllocateVector /
    // AllocateLpi so concurrent device probes can't both grab the same
    // slot and silently drop one handler.
    private static Scheduler.SpinLock s_allocLock;

    /// <summary>
    /// Allocates an unused interrupt vector in [0x40..0xFE], registers
    /// <paramref name="handler"/> for it, and returns the vector. Used by
    /// MSI / MSI-X programmers that need a fresh vector unique to their
    /// device. Throws if the dynamic range is exhausted.
    /// </summary>
    public static byte AllocateVector(IrqDelegate handler)
    {
        if (s_irqHandlers == null)
        {
            throw new System.InvalidOperationException("InterruptManager.Initialize must be called before AllocateVector");
        }

        s_allocLock.Acquire();
        try
        {
            for (int v = s_nextDynamicVector; v <= DynamicVectorMax; v++)
            {
                if (s_irqHandlers[v] == null)
                {
                    s_irqHandlers[v] = handler;
                    s_nextDynamicVector = v + 1;
                    return (byte)v;
                }
            }
            // Wrap once in case earlier vectors were freed.
            for (int v = DynamicVectorMin; v < s_nextDynamicVector; v++)
            {
                if (s_irqHandlers[v] == null)
                {
                    s_irqHandlers[v] = handler;
                    s_nextDynamicVector = v + 1;
                    return (byte)v;
                }
            }
        }
        finally
        {
            s_allocLock.Release();
        }
        throw new System.InvalidOperationException("InterruptManager: dynamic vector range exhausted");
    }

    /// <summary>
    /// Allocates an unused LPI (ARM64 GICv3 ITS), registers
    /// <paramref name="handler"/>, and returns the absolute INTID
    /// (>= <see cref="LpiBase"/>). The matching LPI must still be enabled
    /// in the redistributor's PROPBASER table by the GICv3 LPI driver
    /// before it can fire. Throws on exhaustion.
    /// </summary>
    public static uint AllocateLpi(IrqDelegate handler)
    {
        if (s_lpiHandlers == null)
        {
            throw new System.InvalidOperationException("InterruptManager.Initialize must be called before AllocateLpi");
        }

        s_allocLock.Acquire();
        try
        {
            for (int o = s_nextLpiOffset; o < s_lpiHandlers.Length; o++)
            {
                if (s_lpiHandlers[o] == null)
                {
                    s_lpiHandlers[o] = handler;
                    s_nextLpiOffset = o + 1;
                    return LpiBase + (uint)o;
                }
            }
            for (int o = 0; o < s_nextLpiOffset; o++)
            {
                if (s_lpiHandlers[o] == null)
                {
                    s_lpiHandlers[o] = handler;
                    s_nextLpiOffset = o + 1;
                    return LpiBase + (uint)o;
                }
            }
        }
        finally
        {
            s_allocLock.Release();
        }
        throw new System.InvalidOperationException("InterruptManager: LPI range exhausted");
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

                // Check for spurious interrupt. INTIDs 1020..1023 are
                // GICv3-reserved and 1023 specifically is "spurious"; LPIs
                // start at 8192 and must NOT be treated as spurious.
                if (intId >= 1020 && intId < LpiBase)
                {
                    return;
                }

                // Dispatch: SPI/PPI/SGI -> dense s_irqHandlers[256];
                // LPI (>= 8192) -> sparse s_lpiHandlers[].
                IrqDelegate? handler = null;
                if (intId >= LpiBase)
                {
                    uint off = intId - LpiBase;
                    if (s_lpiHandlers != null && off < (uint)s_lpiHandlers.Length)
                    {
                        handler = s_lpiHandlers[(int)off];
                    }
                }
                else if (s_irqHandlers != null && intId < (uint)s_irqHandlers.Length)
                {
                    handler = s_irqHandlers[(int)intId];
                }
                if (handler != null)
                {
                    ctx.interrupt = intId;
                    handler(ref ctx);
                }

                // Send EOI (works uniformly for SPI/PPI/SGI/LPI).
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
