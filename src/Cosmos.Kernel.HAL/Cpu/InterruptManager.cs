// This code is licensed under MIT license (see LICENSE for details)


using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.Cpu.Data;

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

    internal static IrqDelegate[] s_irqHandlers = new IrqDelegate[256];

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
    /// Registers a handler for a hardware IRQ.
    /// </summary>
    /// <param name="irqNo">IRQ index.</param>
    /// <param name="handler">IRQ handler delegate.</param>
    public static void SetIrqHandler(byte irqNo, IrqDelegate handler)
        => SetHandler((byte)(0x20 + irqNo), handler);

    private const string NewLine = "\n";

    /// <summary>
    /// Called by native bridge from ASM stubs to invoke the proper handler.
    /// </summary>
    /// <param name="ctx">Context structure.</param>
    public static void Dispatch(ref IRQContext ctx)
    {
        Serial.Write("[INT] ", ctx.interrupt, " START", NewLine);
        Serial.Write("[INT] cpu_flags ", ctx.cpu_flags, NewLine);
        Serial.Write("[INT] interrupt ", ctx.interrupt, NewLine);

#if ARCH_ARM64
        Serial.Write("[INT] x0  ", ctx.x0, NewLine);
        Serial.Write("[INT] x1  ", ctx.x1, NewLine);
        Serial.Write("[INT] x2  ", ctx.x2, NewLine);
        Serial.Write("[INT] x3  ", ctx.x3, NewLine);
        Serial.Write("[INT] x29 ", ctx.x29, NewLine);
        Serial.Write("[INT] x30 ", ctx.x30, NewLine);
        Serial.Write("[INT] sp  ", ctx.sp, NewLine);
        Serial.Write("[INT] elr ", ctx.elr, NewLine);
        Serial.Write("[INT] About to check handlers\n");

        // During early boot, halt on sync exceptions to prevent infinite recursion
        // The static array access may cause another exception if memory isn't ready
        if (ctx.interrupt == 0)
        {
            Serial.Write("[INT] FATAL: Sync exception during early boot\n");
            Serial.Write("[INT] ESR: 0x", ctx.cpu_flags.ToString("X"), NewLine);
            Serial.Write("[INT] ELR: 0x", ctx.elr.ToString("X"), NewLine);
            Serial.Write("[INT] FAR: 0x", ctx.far.ToString("X"), NewLine);
            Serial.Write("[INT] Halting.\n");
            while (true) { }
        }
#else
        Serial.Write("[INT] rax ", ctx.rax, NewLine);
        Serial.Write("[INT] rcx ", ctx.rcx, NewLine);
        Serial.Write("[INT] rdx ", ctx.rdx, NewLine);
        Serial.Write("[INT] rbx ", ctx.rbx, NewLine);
        Serial.Write("[INT] rbp ", ctx.rbp, NewLine);
        Serial.Write("[INT] rsi ", ctx.rsi, NewLine);
        Serial.Write("[INT] rdi ", ctx.rdi, NewLine);
        Serial.Write("[INT] r8  ", ctx.r8, NewLine);
        Serial.Write("[INT] r9  ", ctx.r9, NewLine);
        Serial.Write("[INT] r10 ", ctx.r10, NewLine);
        Serial.Write("[INT] r11 ", ctx.r11, NewLine);
        Serial.Write("[INT] r12 ", ctx.r12, NewLine);
        Serial.Write("[INT] r13 ", ctx.r13, NewLine);
        Serial.Write("[INT] r14 ", ctx.r14, NewLine);
        Serial.Write("[INT] r15 ", ctx.r15, NewLine);
#endif

        // Check if handlers array is initialized and interrupt is in valid range
        if (s_irqHandlers != null && ctx.interrupt < (ulong)s_irqHandlers.Length)
        {
            IrqDelegate handler = s_irqHandlers[(int)ctx.interrupt];
            if (handler != null)
            {
                Serial.Write("[INT] Calling registered handler\n");
                handler(ref ctx);
                Serial.Write("[INT] Handler returned\n");
                return;
            }
            else
            {
                Serial.Write("[INT] No handler for vector ", ctx.interrupt, NewLine);
            }
        }
        else
        {
            Serial.Write("[INT] Handler array null or vector out of range\n");
        }

#if ARCH_ARM64
        Serial.Write("[INT] ARM64 unhandled exception path\n");
        // ARM64: Unhandled synchronous exceptions (type 0) are fatal - halt
        // Also halt on SError (type 3) as these are typically hardware errors
        if (ctx.interrupt == 0 || ctx.interrupt == 3)
        {
            Serial.Write("[INT] FATAL: Unhandled ARM64 exception type ", ctx.interrupt, NewLine);
            Serial.Write("[INT] ESR_EL1: 0x", ctx.cpu_flags.ToString("X"), NewLine);
            Serial.Write("[INT] ELR_EL1: 0x", ctx.elr.ToString("X"), NewLine);
            Serial.Write("[INT] System halted.\n");
            while (true) { }
        }
#endif
    }
}
